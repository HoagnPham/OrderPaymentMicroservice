using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class RefundRestartTests
{
    [LocalPostgresFact]
    public async Task KilledApiRecoversRefundWithoutRefundingOrReleasingInventoryTwice()
    {
        await using var postgres=await LocalPostgresServer.StartAsync();
        var root=Path.Combine(Path.GetTempPath(),"refund-restart-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        OperationalProbe? host=null;Task<HttpResponseMessage>? pending=null;
        AppDbContext Database()=>new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        try
        {
            var ledger=Path.Combine(root,"ledger");var marker=Path.Combine(root,"refunded.marker");
            host=await OperationalProbe.Start(postgres.ConnectionString,ledger,marker:marker,operation:"refund");
            var user=await host.Account();var product=(await host.Client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
            using var created=await host.Client.PostAsJsonAsync("api/orders",new CreateOrderRequest{TotalAmount=product.Price,Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}]});created.EnsureSuccessStatusCode();
            var order=(await created.Content.ReadFromJsonAsync<OrderResponse>())!;
            using var charge=new HttpRequestMessage(HttpMethod.Post,"api/payments"){Content=JsonContent.Create(new CreatePaymentRequest{OrderId=order.Id,Amount=order.TotalAmount})};charge.Headers.Add("Idempotency-Key","refund-charge-"+order.Id);
            using var charged=await host.Client.SendAsync(charge);charged.EnsureSuccessStatusCode();var payment=(await charged.Content.ReadFromJsonAsync<PaymentResponse>())!;Assert.Equal("SUCCESS",payment.Status);
            await using(var db=Database()){(await db.Users.FindAsync(user.Id))!.Role="Admin";await db.SaveChangesAsync();}
            using var logged=await host.Client.PostAsJsonAsync("api/users/login",new LoginRequest{Email=user.Email,Password="Operations-Password!123"});logged.EnsureSuccessStatusCode();var admin=(await logged.Content.ReadFromJsonAsync<LoginResponse>())!;host.Authorize(admin.Token);
            var key="refund-restart-"+Guid.NewGuid();
            using var refund=new HttpRequestMessage(HttpMethod.Post,$"api/payments/{payment.Id}/refund");refund.Headers.Add("Idempotency-Key",key);
            pending=host.Client.SendAsync(refund);await host.Wait(()=>Task.FromResult(File.Exists(marker)));var refundTransaction=await File.ReadAllTextAsync(marker);
            var hashes=Directory.GetFiles(ledger,"*.json").ToDictionary(p=>Path.GetFileName(p)!,p=>OperationalProbe.Hash(File.ReadAllBytes(p)));Assert.Equal(2,hashes.Count);
            DateTime? pendingSince;
            await using(var db=Database())
            {
                var stored=(await db.Payments.FindAsync(payment.Id))!;Assert.Equal(PaymentStatus.REFUND_PENDING,stored.Status);pendingSince=stored.PendingSince;Assert.NotNull(pendingSince);
                Assert.Equal(InventoryReservationStatus.RESERVED,(await db.Orders.FindAsync(order.Id))!.InventoryStatus);Assert.Equal(product.Stock-1,(await db.Products.FindAsync(product.Id))!.Stock);
                stored.UpdatedAt=DateTime.UtcNow.AddMinutes(-5);await db.SaveChangesAsync();
            }
            var firstPid=host.Pid;await host.DisposeAsync();host=null;
            try{using var interrupted=await pending;}catch(HttpRequestException){}catch(OperationCanceledException){}pending=null;
            host=await OperationalProbe.Start(postgres.ConnectionString,ledger,recovery:true);host.Authorize(admin.Token);Assert.NotEqual(firstPid,host.Pid);
            await host.Wait(async()=>{await using var db=Database();return await db.Payments.AnyAsync(p=>p.Id==payment.Id&&p.Status==PaymentStatus.REFUNDED)&&await db.AuditEvents.AnyAsync(e=>e.ResourceType=="PAYMENT"&&e.ResourceId==payment.Id&&e.ActorKind=="SYSTEM"&&e.Phase=="RESULT"&&e.Outcome=="REFUNDED");});
            await using(var db=Database())
            {
                var stored=(await db.Payments.FindAsync(payment.Id))!;Assert.Equal(payment.TransactionCode,stored.TransactionCode);Assert.Equal(refundTransaction,stored.RefundTransactionCode);Assert.Null(stored.PendingSince);Assert.Equal(1,stored.AttemptCount);
                Assert.Equal(OrderStatus.REFUNDED,(await db.Orders.FindAsync(order.Id))!.Status);Assert.Equal(InventoryReservationStatus.RELEASED,(await db.Orders.FindAsync(order.Id))!.InventoryStatus);Assert.Equal(product.Stock,(await db.Products.FindAsync(product.Id))!.Stock);
            }
            using var replay=new HttpRequestMessage(HttpMethod.Post,$"api/payments/{payment.Id}/refund");replay.Headers.Add("Idempotency-Key",key);
            using var repeated=await host.Client.SendAsync(replay);Assert.Equal(HttpStatusCode.OK,repeated.StatusCode);Assert.Equal("REFUNDED",(await repeated.Content.ReadFromJsonAsync<PaymentResponse>())!.Status);
            await using(var db=Database())Assert.Equal(product.Stock,(await db.Products.FindAsync(product.Id))!.Stock);
            var after=Directory.GetFiles(ledger,"*.json").ToDictionary(p=>Path.GetFileName(p)!,p=>OperationalProbe.Hash(File.ReadAllBytes(p)));Assert.Equal(2,after.Count);foreach(var item in hashes)Assert.Equal(item.Value,after[item.Key]);
            using var status=await host.Client.GetAsync("api/operations/status");status.EnsureSuccessStatusCode();using var metrics=JsonDocument.Parse(await status.Content.ReadAsStringAsync());Assert.True(metrics.RootElement.GetProperty("process").GetProperty("recovery_resolved").GetInt64()>=1);
            // Stop only this temporary DB to prove liveness differs from DB readiness.
            await postgres.DisposeAsync();
            using var live=await host.Client.GetAsync("health/live");Assert.Equal(HttpStatusCode.OK,live.StatusCode);
            using var ready=await host.Client.GetAsync("health/ready");Assert.Equal(HttpStatusCode.ServiceUnavailable,ready.StatusCode);Assert.Equal("{\"status\":\"unavailable\"}",await ready.Content.ReadAsStringAsync());
            using var unavailable=await host.Client.GetAsync("api/operations/status");Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);Assert.DoesNotContain(postgres.ConnectionString,await unavailable.Content.ReadAsStringAsync());
            await OperationalProbe.Evidence("refund_restart.results.json",new{passed=true,run_at_utc=DateTime.UtcNow,first_pid=firstPid,restarted_pid=host.Pid,kill_window="Refund journal published; local payment REFUND_PENDING and inventory RESERVED",recovered_by="PaymentRecoveryWorker",journal_entries=2,journal_hashes=hashes,refund_journal_unchanged=true,charge_reference_preserved=true,inventory_released_once=true,pending_since_cleared=true,health_database_outage_verified=true,
                source_sha256=OperationalProbe.SourceHashes("OrderPaymentMicroservice.IntegrationTests/RefundRestartTests.cs","OrderPaymentMicroservice.IntegrationTests/OperationalProbe.cs","OrderPaymentMicroservice.CrashProbe/CrashProbeHost.cs","OrderPaymentMicroservice/Services/Payments/PaymentRecoveryWorker.cs","OrderPaymentMicroservice/Services/PaymentService.cs","OrderPaymentMicroservice/Program.cs"),
                limitations=new[]{"Local simulated provider and abrupt API kill after journal publish; no real provider, power loss or network partition.","Database outage is a stopped temporary cluster; no failover/PITR or recovery after reconnect tested."}});
        }
        finally{if(host!=null)await host.DisposeAsync();if(pending!=null){try{using var r=await pending;}catch{}}if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
