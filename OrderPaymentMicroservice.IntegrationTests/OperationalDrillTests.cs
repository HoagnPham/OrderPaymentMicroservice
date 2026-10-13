using System.Diagnostics;
using System.Formats.Tar;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class OperationalDrillTests
{
    private static AppDbContext Database(string connection)=>new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    [LocalPostgresFact]
    public async Task BackupRestoresDatabaseReceiptsAuditAndGatewayJournalThenRecoversPendingCharge()
    {
        await using var postgres=await LocalPostgresServer.StartAsync();
        var root=Path.Combine(Path.GetTempPath(),"backup-drill-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        OperationalProbe? source=null;OperationalProbe? restored=null;Task<HttpResponseMessage>? pending=null;
        try
        {
            var ledger=Path.Combine(root,"source-ledger");var restoredLedger=Path.Combine(root,"restored-ledger");var marker=Path.Combine(root,"charge.marker");
            source=await OperationalProbe.Start(postgres.ConnectionString,ledger,marker:marker);
            var user=await source.Account();var product=(await source.Client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
            var orderRequest=new CreateOrderRequest{TotalAmount=product.Price,Address="Restore address",City="Restore city",Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}]};
            var creationKey="restore-order-"+Guid.NewGuid();
            using var create=new HttpRequestMessage(HttpMethod.Post,"api/orders"){Content=JsonContent.Create(orderRequest)};create.Headers.Add("Idempotency-Key",creationKey);
            using var created=await source.Client.SendAsync(create);created.EnsureSuccessStatusCode();
            var receipt=await created.Content.ReadAsStringAsync();var order=(await created.Content.ReadFromJsonAsync<OrderResponse>())!;
            using var pay=new HttpRequestMessage(HttpMethod.Post,"api/payments"){Content=JsonContent.Create(new CreatePaymentRequest{OrderId=order.Id,Amount=order.TotalAmount})};pay.Headers.Add("Idempotency-Key","restore-charge-"+order.Id);
            pending=source.Client.SendAsync(pay);await source.Wait(()=>Task.FromResult(File.Exists(marker)));
            var transaction=await File.ReadAllTextAsync(marker);
            await using(var db=Database(postgres.ConnectionString))
            {
                Assert.Equal(PaymentStatus.PROCESSING,(await db.Payments.SingleAsync(p=>p.OrderId==order.Id)).Status);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Payments\" SET \"UpdatedAt\" = {DateTime.UtcNow.AddMinutes(-5)} WHERE \"OrderId\" = {order.Id}");
            }
            await source.DisposeAsync();source=null;
            try{using var response=await pending;}catch(HttpRequestException){}catch(OperationCanceledException){}
            pending=null;
            // Source API/worker are stopped; no writers exist during the paired snapshot.
            var baseline=await TableHashes(postgres.ConnectionString);
            var journal=Directory.GetFiles(ledger,"*.json").Single();var journalHash=OperationalProbe.Hash(await File.ReadAllBytesAsync(journal));
            var archive=Path.Combine(root,"database.dump");var ledgerArchive=Path.Combine(root,"ledger.tar");
            var sourceInfo=new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
            var backupTime=Stopwatch.StartNew();
            await Pg("pg_dump",sourceInfo,"--format=custom","--file",archive);
            TarFile.CreateFromDirectory(ledger,ledgerArchive,includeBaseDirectory:false);backupTime.Stop();
            await Pg("pg_restore",null,"--list",archive);
            var targetInfo=new NpgsqlConnectionStringBuilder(postgres.ConnectionString){Database="restore_"+Guid.NewGuid().ToString("N")};
            await Pg("createdb",sourceInfo,targetInfo.Database);
            var restoreTime=Stopwatch.StartNew();
            await Pg("pg_restore",targetInfo,"--exit-on-error","--dbname",targetInfo.Database,archive);
            Directory.CreateDirectory(restoredLedger);TarFile.ExtractToDirectory(ledgerArchive,restoredLedger,overwriteFiles:false);restoreTime.Stop();
            var restoredHashes=await TableHashes(targetInfo.ConnectionString);
            foreach(var table in baseline)Assert.Equal(table.Value,restoredHashes[table.Key]);
            Assert.Equal(journalHash,OperationalProbe.Hash(await File.ReadAllBytesAsync(Directory.GetFiles(restoredLedger,"*.json").Single())));
            var recoveryTime=Stopwatch.StartNew();restored=await OperationalProbe.Start(targetInfo.ConnectionString,restoredLedger,recovery:true);restored.Authorize(user.Token);
            await restored.Wait(async()=>{await using var db=Database(targetInfo.ConnectionString);return await db.Payments.AnyAsync(p=>p.OrderId==order.Id&&p.Status==PaymentStatus.SUCCESS);});recoveryTime.Stop();
            await using(var db=Database(targetInfo.ConnectionString))
            {
                var payment=await db.Payments.SingleAsync(p=>p.OrderId==order.Id);Assert.Equal(transaction,payment.TransactionCode);Assert.Equal(1,payment.AttemptCount);
                Assert.Equal(OrderStatus.PAID,(await db.Orders.FindAsync(order.Id))!.Status);Assert.Equal(product.Stock-1,(await db.Products.FindAsync(product.Id))!.Stock);
                Assert.True(await db.AuditEvents.AnyAsync(e=>e.Phase=="COMMIT"&&e.ResourceType=="PAYMENT"&&e.ResourceId==payment.Id));
                Assert.Equal(4,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger WHERE tgname IN ('orders_atomic_audit','payments_atomic_audit','audit_append_only','audit_no_truncate') AND tgenabled = 'O'").SingleAsync());
            }
            using var replayMessage=new HttpRequestMessage(HttpMethod.Post,"api/orders"){Content=JsonContent.Create(orderRequest)};replayMessage.Headers.Add("Idempotency-Key",creationKey);
            using var replay=await restored.Client.SendAsync(replayMessage);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Equal(receipt,await replay.Content.ReadAsStringAsync());
            using var newMessage=new HttpRequestMessage(HttpMethod.Post,"api/orders"){Content=JsonContent.Create(orderRequest)};newMessage.Headers.Add("Idempotency-Key","after-restore-"+Guid.NewGuid());
            using var next=await restored.Client.SendAsync(newMessage);Assert.Equal(HttpStatusCode.Created,next.StatusCode);Assert.True((await next.Content.ReadFromJsonAsync<OrderResponse>())!.Id>order.Id);
            Assert.Single(Directory.GetFiles(restoredLedger,"*.json"));Assert.Equal(journalHash,OperationalProbe.Hash(await File.ReadAllBytesAsync(Directory.GetFiles(restoredLedger,"*.json").Single())));
            await using(var db=Database(postgres.ConnectionString))Assert.Equal(PaymentStatus.PROCESSING,(await db.Payments.SingleAsync(p=>p.OrderId==order.Id)).Status);
            var sourceAfter=await TableHashes(postgres.ConnectionString);foreach(var table in baseline)Assert.Equal(table.Value,sourceAfter[table.Key]);
            await OperationalProbe.Evidence("backup_restore.results.json",new{passed=true,run_at_utc=DateTime.UtcNow,backup_ms=backupTime.Elapsed.TotalMilliseconds,restore_ms=restoreTime.Elapsed.TotalMilliseconds,restored_api_recovery_ms=recoveryTime.Elapsed.TotalMilliseconds,
                database_archive_sha256=OperationalProbe.Hash(await File.ReadAllBytesAsync(archive)),database_archive_bytes=new FileInfo(archive).Length,ledger_archive_sha256=OperationalProbe.Hash(await File.ReadAllBytesAsync(ledgerArchive)),journal_sha256=journalHash,
                table_hashes=baseline,restored_table_hashes=restoredHashes,enabled_audit_triggers=4,receipt_replayed=true,identity_sequence_can_allocate_new_order=true,charge_records=1,attempt_count=1,source_database_unchanged_after_restore=true,
                source_sha256=OperationalProbe.SourceHashes("OrderPaymentMicroservice.IntegrationTests/OperationalDrillTests.cs","OrderPaymentMicroservice.IntegrationTests/OperationalProbe.cs","OrderPaymentMicroservice/Services/Payments/DurableFakePaymentGateway.cs","OrderPaymentMicroservice/Services/OrderService.cs"),
                limitations=new[]{"Temporary local PostgreSQL cluster, stopped writers and one local ledger; no live production backup or point-in-time recovery.","Timing describes this fixture, not committed production RPO/RTO. Archives with test user data are removed after assertions; only hashes remain."}});
        }
        finally
        {
            if(source!=null)await source.DisposeAsync();if(restored!=null)await restored.DisposeAsync();
            if(pending!=null){try{using var r=await pending;}catch{}}
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    [LocalPostgresFact]
    public async Task TwoRealApiProcessesRecordBoundedLoadLatencyConflictsAndRateLimits()
    {
        await using var postgres=await LocalPostgresServer.StartAsync();
        var root=Path.Combine(Path.GetTempPath(),"load-drill-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            await using var first=await OperationalProbe.Start(postgres.ConnectionString,Path.Combine(root,"ledger"));
            var user=await first.Account();
            await using var second=await OperationalProbe.Start(postgres.ConnectionString,Path.Combine(root,"ledger"));second.Authorize(user.Token);
            int productId;
            await using(var db=Database(postgres.ConnectionString)){var product=new Product{Name="Load fixture",Price=10,Stock=1000};db.Products.Add(product);await db.SaveChangesAsync();productId=product.Id;}
            var clients=new[]{first.Client,second.Client};
            var reads=await Load(200,8,i=>clients[i%2].GetAsync("api/orders/mine"));Assert.All(reads.Samples,s=>Assert.Equal(200,s.Status));
            var orderRequest=new CreateOrderRequest{TotalAmount=10,Items=[new(){ProductId=productId,Quantity=1,UnitPrice=10}]};
            var writes=await Load(96,8,i=>{var m=new HttpRequestMessage(HttpMethod.Post,"api/orders"){Content=JsonContent.Create(orderRequest)};m.Headers.Add("Idempotency-Key","load-"+Guid.NewGuid());return Send(clients[i%2],m);});
            Assert.All(writes.Samples,s=>Assert.True(s.Status is 201 or 409,s.Status.ToString()));var created=writes.Samples.Count(s=>s.Status==201);Assert.True(created>0);
            await using(var db=Database(postgres.ConnectionString))
            {Assert.Equal(created,await db.Orders.CountAsync(o=>o.CustomerId==user.Id));Assert.Equal(1000-created,(await db.Products.FindAsync(productId))!.Stock);Assert.Equal(created,await db.AuditEvents.CountAsync(e=>e.Phase=="COMMIT"&&e.Action=="ORDER_INSERT"&&e.ActorUserId==user.Id));}
            await using var limited=await OperationalProbe.Start(postgres.ConnectionString,Path.Combine(root,"ledger"),permits:5);limited.Authorize(user.Token);
            var rate=await Load(20,4,_=>limited.Client.GetAsync("api/orders/mine"));Assert.Equal(5,rate.Samples.Count(s=>s.Status==200));Assert.Equal(15,rate.Samples.Count(s=>s.Status==429));
            await OperationalProbe.Evidence("load_probe.results.json",new{passed=true,run_at_utc=DateTime.UtcNow,api_processes=2,first_pid=first.Pid,second_pid=second.Pid,read=reads.Summary(),write=writes.Summary(),rate_limit=rate.Summary(),created_orders=created,final_stock=1000-created,
                source_sha256=OperationalProbe.SourceHashes("OrderPaymentMicroservice.IntegrationTests/OperationalDrillTests.cs","OrderPaymentMicroservice.IntegrationTests/OperationalProbe.cs","OrderPaymentMicroservice/Services/OrderService.cs","OrderPaymentMicroservice/Program.cs"),
                limitations=new[]{"Short localhost workload on one machine; no warmup exclusion, repetitions, saturation sweep or soak test.","Read/write quota raised to 100000 to isolate DB contention; separate single-process scenario uses quota 5/minute.","409 responses are one-shot attempts without retry; throughput includes rejected requests and is not orders/second.","Payment backlog recovery latency is measured separately in backup_restore.results.json, not under concurrent load."}});
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client,HttpRequestMessage message){using(message)return await client.SendAsync(message);}
    private sealed record Sample(int Status,double Milliseconds);
    private sealed record LoadResult(Sample[] Samples,double Seconds,int Concurrency)
    {
        public object Summary(){var times=Samples.Select(s=>s.Milliseconds).Order().ToArray();double P(double p)=>times[(int)Math.Ceiling(p*times.Length)-1];return new{requests=Samples.Length,concurrency=Concurrency,duration_seconds=Seconds,requests_per_second=Samples.Length/Seconds,successful_requests_per_second=Samples.Count(s=>s.Status<400)/Seconds,samples=Samples.Select(s=>new{status=s.Status,elapsed_ms=s.Milliseconds}),mean_ms=times.Average(),p50_ms=P(.5),p95_ms=P(.95),p99_ms=P(.99),status_counts=Samples.GroupBy(s=>s.Status).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),latency_definition="Request start after concurrency semaphore through complete response body; excludes queue wait; percentile uses nearest rank"};}
    }
    private static async Task<LoadResult> Load(int count,int concurrency,Func<int,Task<HttpResponseMessage>> send)
    {
        using var gate=new SemaphoreSlim(concurrency);var total=Stopwatch.StartNew();
        var samples=await Task.WhenAll(Enumerable.Range(0,count).Select(async i=>{await gate.WaitAsync();try{var timer=Stopwatch.StartNew();using var response=await send(i);await response.Content.ReadAsByteArrayAsync();return new Sample((int)response.StatusCode,timer.Elapsed.TotalMilliseconds);}finally{gate.Release();}}));
        total.Stop();return new(samples,total.Elapsed.TotalSeconds,concurrency);
    }
    private static async Task<Dictionary<string,string>> TableHashes(string connection)
    {
        await using var db=new NpgsqlConnection(connection);await db.OpenAsync();var result=new Dictionary<string,string>();
        foreach(var table in new[]{"Users","Products","Orders","OrderItems","Payments","AuditEvents","ShoppingCarts","CartItems","__EFMigrationsHistory"})
        {
            var key=table=="__EFMigrationsHistory"?"MigrationId":"Id";await using var command=new NpgsqlCommand($"SELECT row_to_json(t)::text FROM \"{table}\" t ORDER BY \"{key}\"",db);
            await using var reader=await command.ExecuteReaderAsync();var rows=new StringBuilder();while(await reader.ReadAsync())rows.AppendLine(reader.GetString(0));
            result[table]=OperationalProbe.Hash(Encoding.UTF8.GetBytes(rows.ToString()));
        }
        return result;
    }
    private static async Task Pg(string executable,NpgsqlConnectionStringBuilder? db,params string[] args)
    {
        var info=new ProcessStartInfo(Path.Combine(LocalPostgresServer.FindBinDirectory()!,executable)){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        if(db!=null){info.ArgumentList.Add("--host");info.ArgumentList.Add(db.Host!);info.ArgumentList.Add("--port");info.ArgumentList.Add(db.Port.ToString());info.ArgumentList.Add("--username");info.ArgumentList.Add(db.Username!);}
        if(executable=="pg_dump"){info.ArgumentList.Add("--dbname");info.ArgumentList.Add(db!.Database!);}
        foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        await output;Assert.True(process.ExitCode==0,await error);
    }
}
