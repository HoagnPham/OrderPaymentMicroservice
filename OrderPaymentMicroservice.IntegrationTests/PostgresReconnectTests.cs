using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class PostgresReconnectTests
{
    [LocalPostgresFact]
    public Task ChargeRecoversAfterDatabaseReconnectWithoutAnotherCharge()=>Run("charge");
    [LocalPostgresFact]
    public Task RefundRecoversAfterDatabaseReconnectWithoutAnotherRefundOrStockRelease()=>Run("refund");

    private static async Task Run(string operation)
    {
        await using var postgres=await LocalPostgresServer.StartAsync();
        var connection=new NpgsqlConnectionStringBuilder(postgres.ConnectionString){Pooling=true,Timeout=2,CommandTimeout=2}.ConnectionString;
        var root=Path.Combine(Path.GetTempPath(),"postgres-reconnect-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        OperationalProbe? host=null;Task<HttpResponseMessage>? pending=null;
        // The API keeps its original pool throughout the outage. The external oracle
        // opens fresh connections so stale test-runner sockets cannot mask its result.
        var observerConnection=new NpgsqlConnectionStringBuilder(connection){Pooling=false}.ConnectionString;
        AppDbContext Database()=>new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(observerConnection).Options);
        var timer=Stopwatch.StartNew();var timeline=new List<object>();
        void Step(string name)=>timeline.Add(new{step=name,elapsed_ms=timer.Elapsed.TotalMilliseconds});
        try
        {
            var ledger=Path.Combine(root,"ledger");var marker=Path.Combine(root,"gateway.marker");
            var release=Path.Combine(root,"release.marker");var counts=Path.Combine(root,"gateway-counts.json");
            host=await OperationalProbe.Start(connection,ledger,recovery:true,marker:marker,operation:operation,releaseFile:release,countsFile:counts);
            var apiPid=host.Pid;var user=await host.Account();
            await using(var db=Database()){(await db.Users.FindAsync(user.Id))!.Role="Admin";await db.SaveChangesAsync();}
            using var login=await host.Client.PostAsJsonAsync("api/users/login",new LoginRequest{Email=user.Email,Password="Operations-Password!123"});login.EnsureSuccessStatusCode();
            host.Authorize((await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
            using var readyBefore=await host.Client.GetAsync("health/ready");Assert.Equal(HttpStatusCode.OK,readyBefore.StatusCode);Step("READY_200_BEFORE");
            var product=(await host.Client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
            using var created=await host.Client.PostAsJsonAsync("api/orders",new CreateOrderRequest{TotalAmount=product.Price,Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}]});created.EnsureSuccessStatusCode();
            var order=(await created.Content.ReadFromJsonAsync<OrderResponse>())!;
            var chargeKey="reconnect-charge-"+Guid.NewGuid();var refundKey="reconnect-refund-"+Guid.NewGuid();
            using var charge=new HttpRequestMessage(HttpMethod.Post,"api/payments"){Content=JsonContent.Create(new CreatePaymentRequest{OrderId=order.Id,Amount=order.TotalAmount})};charge.Headers.Add("Idempotency-Key",chargeKey);
            int paymentId;
            if(operation=="refund")
            {
                using var charged=await host.Client.SendAsync(charge);charged.EnsureSuccessStatusCode();
                paymentId=(await charged.Content.ReadFromJsonAsync<PaymentResponse>())!.Id;
                using var refund=new HttpRequestMessage(HttpMethod.Post,$"api/payments/{paymentId}/refund");refund.Headers.Add("Idempotency-Key",refundKey);
                pending=host.Client.SendAsync(refund);
                await host.Wait(()=>Task.FromResult(File.Exists(marker)));
            }
            else
            {
                pending=host.Client.SendAsync(charge);await host.Wait(()=>Task.FromResult(File.Exists(marker)));
                await using var db=Database();paymentId=(await db.Payments.SingleAsync(p=>p.OrderId==order.Id)).Id;
            }
            var transaction=await File.ReadAllTextAsync(marker);Step("GATEWAY_JOURNAL_PUBLISHED_LOCAL_FINALIZE_BLOCKED");
            var journalBefore=Directory.GetFiles(ledger,"*.json").ToDictionary(p=>Path.GetFileName(p)!,p=>OperationalProbe.Hash(File.ReadAllBytes(p)));
            Assert.Equal(operation=="refund"?2:1,journalBefore.Count);
            string? chargeReference;
            await using(var db=Database())
            {
                var payment=(await db.Payments.FindAsync(paymentId))!;
                Assert.Equal(operation=="refund"?PaymentStatus.REFUND_PENDING:PaymentStatus.PROCESSING,payment.Status);
                Assert.Equal(chargeKey,payment.IdempotencyKey);if(operation=="refund")Assert.Equal(refundKey,payment.RefundIdempotencyKey);
                chargeReference=payment.TransactionCode;
                Assert.Equal(InventoryReservationStatus.RESERVED,(await db.Orders.FindAsync(order.Id))!.InventoryStatus);
                Assert.Equal(product.Stock-1,(await db.Products.FindAsync(product.Id))!.Stock);
            }
            await postgres.StopAsync();Step("POSTGRES_STOPPED_DATA_DIRECTORY_RETAINED");
            using var live=await host.Client.GetAsync("health/live");Assert.Equal(HttpStatusCode.OK,live.StatusCode);
            using var readyOff=await host.Client.GetAsync("health/ready");Assert.Equal(HttpStatusCode.ServiceUnavailable,readyOff.StatusCode);Step("READY_503_LIVE_200");
            await File.WriteAllTextAsync(release,"continue");
            using var interrupted=await pending.WaitAsync(TimeSpan.FromSeconds(10));pending=null;
            Assert.Equal(HttpStatusCode.ServiceUnavailable,interrupted.StatusCode);Step("ORIGINAL_FINALIZE_FAILED_WHILE_DB_OFF");
            // A repeated request while DB is unavailable cannot reach a second gateway mutation.
            using var whileOff=new HttpRequestMessage(HttpMethod.Post,operation=="refund"?$"api/payments/{paymentId}/refund":"api/payments");
            if(operation=="charge")whileOff.Content=JsonContent.Create(new CreatePaymentRequest{OrderId=order.Id,Amount=order.TotalAmount});
            whileOff.Headers.Add("Idempotency-Key",operation=="refund"?refundKey:chargeKey);
            using var rejected=await host.Client.SendAsync(whileOff);Assert.Equal(HttpStatusCode.ServiceUnavailable,rejected.StatusCode);
            // Allow at least one 5-second recovery scan to encounter the real DB outage.
            await Task.Delay(TimeSpan.FromSeconds(6.5));Step("RECOVERY_SCAN_DURING_OUTAGE");
            await postgres.RestartAsync();Step("SAME_POSTGRES_CLUSTER_RESTARTED");
            await host.Wait(async()=>{using var r=await host.Client.GetAsync("health/ready");return r.StatusCode==HttpStatusCode.OK;});Step("READY_200_AFTER");
            var target=operation=="refund"?PaymentStatus.REFUNDED:PaymentStatus.SUCCESS;
            await host.Wait(async()=>
            {
                await using var db=Database();return await db.Payments.AnyAsync(p=>p.Id==paymentId&&p.Status==target)
                    &&await db.AuditEvents.AnyAsync(e=>e.ActorKind=="SYSTEM"&&e.ResourceType=="PAYMENT"&&e.ResourceId==paymentId&&e.Phase=="RESULT"&&e.Outcome==target.ToString());
            });Step("WORKER_RECONCILED_WITHOUT_API_RESTART");
            Assert.Equal(apiPid,host.Pid);
            await using(var db=Database())
            {
                var payment=(await db.Payments.FindAsync(paymentId))!;var stored=(await db.Orders.FindAsync(order.Id))!;
                Assert.Equal(1,await db.Payments.CountAsync(p=>p.OrderId==order.Id));Assert.Equal(1,payment.AttemptCount);
                Assert.Equal(chargeKey,payment.IdempotencyKey);
                Assert.Equal(operation=="refund"?OrderStatus.REFUNDED:OrderStatus.PAID,stored.Status);
                Assert.Equal(operation=="refund"?InventoryReservationStatus.RELEASED:InventoryReservationStatus.RESERVED,stored.InventoryStatus);
                Assert.Equal(operation=="refund"?product.Stock:product.Stock-1,(await db.Products.FindAsync(product.Id))!.Stock);
                if(operation=="refund")
                {Assert.Equal(chargeReference,payment.TransactionCode);Assert.Equal(refundKey,payment.RefundIdempotencyKey);Assert.Equal(transaction,payment.RefundTransactionCode);}
                else Assert.Equal(transaction,payment.TransactionCode);
            }
            using var replay=new HttpRequestMessage(HttpMethod.Post,operation=="refund"?$"api/payments/{paymentId}/refund":"api/payments");
            if(operation=="charge")replay.Content=JsonContent.Create(new CreatePaymentRequest{OrderId=order.Id,Amount=order.TotalAmount});
            replay.Headers.Add("Idempotency-Key",operation=="refund"?refundKey:chargeKey);
            using var repeated=await host.Client.SendAsync(replay);repeated.EnsureSuccessStatusCode();Assert.Equal(target.ToString(),(await repeated.Content.ReadFromJsonAsync<PaymentResponse>())!.Status);
            var journalAfter=Directory.GetFiles(ledger,"*.json").ToDictionary(p=>Path.GetFileName(p)!,p=>OperationalProbe.Hash(File.ReadAllBytes(p)));
            Assert.Equal(journalBefore.Count,journalAfter.Count);foreach(var entry in journalBefore)Assert.Equal(entry.Value,journalAfter[entry.Key]);
            using var callDoc=JsonDocument.Parse(await File.ReadAllTextAsync(counts));
            Assert.Equal(1,callDoc.RootElement.GetProperty("charge_calls").GetInt32());Assert.Equal(operation=="refund"?1:0,callDoc.RootElement.GetProperty("refund_calls").GetInt32());
            Assert.True(callDoc.RootElement.GetProperty(operation=="refund"?"refund_status_lookups":"charge_status_lookups").GetInt32()>=1);
            using var metricsResponse=await host.Client.GetAsync("api/operations/status");metricsResponse.EnsureSuccessStatusCode();using var metrics=JsonDocument.Parse(await metricsResponse.Content.ReadAsStringAsync());
            Assert.True(metrics.RootElement.GetProperty("process").GetProperty("recovery_scan_failures").GetInt64()>=1);Assert.True(metrics.RootElement.GetProperty("process").GetProperty("recovery_resolved").GetInt64()>=1);
            int finalStock, reservationCommits, releaseCommits;
            string finalPaymentStatus, finalOrderStatus, finalInventoryStatus;
            await using(var db=Database())
            {
                finalStock=(await db.Products.FindAsync(product.Id))!.Stock;
                Assert.Equal(operation=="refund"?product.Stock:product.Stock-1,finalStock);
                reservationCommits=await db.AuditEvents.CountAsync(e=>e.ResourceType=="ORDER"&&e.ResourceId==order.Id&&e.Phase=="COMMIT"&&e.Action=="ORDER_INSERT");
                releaseCommits=await db.AuditEvents.CountAsync(e=>e.ResourceType=="ORDER"&&e.ResourceId==order.Id&&e.Phase=="COMMIT"&&e.Action=="ORDER_UPDATE"&&e.Outcome=="STATE_"+((int)OrderStatus.REFUNDED).ToString());
                Assert.Equal(1,reservationCommits);Assert.Equal(operation=="refund"?1:0,releaseCommits);
                finalPaymentStatus=(await db.Payments.FindAsync(paymentId))!.Status.ToString();
                var finalOrder=(await db.Orders.FindAsync(order.Id))!;
                finalOrderStatus=finalOrder.Status.ToString();finalInventoryStatus=finalOrder.InventoryStatus.ToString();
            }
            Step("SAME_KEY_REPLAY_NO_ADDITIONAL_GATEWAY_MUTATION");
            await OperationalProbe.Evidence($"postgres_reconnect_{operation}.results.json",new
            {
                passed=true,run_at_utc=DateTime.UtcNow,operation,api_pid=apiPid,api_restarted=false,postgres_data_preserved=true,connection_pooling=true,
                health_ready_sequence=new[]{200,503,200},health_live_during_outage=200,original_finalize_http_status=503,worker_metrics=metrics.RootElement.GetProperty("process").Clone(),
                gateway_calls=callDoc.RootElement.Clone(),payment_records=1,attempt_count=1,idempotency_keys_preserved=true,journal_hashes=journalAfter,
                inventory_reservation_count=reservationCommits,inventory_release_count=releaseCommits,charge_reference_preserved=operation=="refund"?(bool?)true:null,
                final_payment_status=finalPaymentStatus,final_order_status=finalOrderStatus,final_inventory_status=finalInventoryStatus,initial_stock=product.Stock,final_stock=finalStock,timeline,
                source_sha256=OperationalProbe.SourceHashes("OrderPaymentMicroservice.IntegrationTests/PostgresReconnectTests.cs","OrderPaymentMicroservice.IntegrationTests/LocalPostgresServer.cs","OrderPaymentMicroservice.IntegrationTests/OperationalProbe.cs","OrderPaymentMicroservice.CrashProbe/CrashProbeHost.cs","OrderPaymentMicroservice/Services/Payments/PaymentRecoveryWorker.cs","OrderPaymentMicroservice/Services/PaymentService.cs","OrderPaymentMicroservice/Services/Payments/DurableFakePaymentGateway.cs"),
                limitations=new[]{"Real stop/start of one temporary local PostgreSQL cluster, same API process, simulated durable provider; not network partition, failover or real PSP.","Test executable barrier creates the pre-finalize window; production executable has no fault switches. No DB role/retention/external alert changes."}
            });
        }
        finally
        {
            if(host!=null)await host.DisposeAsync();if(pending!=null){try{using var response=await pending;}catch{}}
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
