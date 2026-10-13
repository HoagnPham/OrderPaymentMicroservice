using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Services.Payments;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class OperationsTests(PostgresApiFixture fixture)
{
    [DockerFact]
    public async Task HealthIsMinimalAndOperationalDetailsRequireAdmin()
    {
        using var client=fixture.CreateClient();
        using var live=await client.GetAsync("health/live");Assert.Equal(HttpStatusCode.OK,live.StatusCode);Assert.Equal("{\"status\":\"alive\"}",await live.Content.ReadAsStringAsync());Assert.True(live.Headers.CacheControl!.NoStore);
        using var ready=await client.GetAsync("health/ready");Assert.Equal(HttpStatusCode.OK,ready.StatusCode);Assert.Equal("{\"status\":\"ready\"}",await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("api/operations/status")).StatusCode);
        var user=await Account();client.DefaultRequestHeaders.Authorization=new("Bearer",user.Token);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("api/operations/status")).StatusCode);
        var admin=await Promote(user);client.DefaultRequestHeaders.Authorization=new("Bearer",admin.Token);
        using var details=await client.GetAsync("api/operations/status");Assert.Equal(HttpStatusCode.OK,details.StatusCode);Assert.True(details.Headers.CacheControl!.NoStore);
        var json=await details.Content.ReadAsStringAsync();Assert.DoesNotContain(user.Token,json);Assert.DoesNotContain(user.Email,json);Assert.DoesNotContain("OperationsTestPassword!123",json);
    }

    [DockerFact]
    public async Task ReconciliationRefreshDoesNotHidePendingAgeAndMissingAuditRaisesAlerts()
    {
        var user=await Account();var admin=await Promote(user);
        await using var factory=fixture.CreateIsolatedFactory(new(){["Operations:AlertPollingEnabled"]="false"},services=>
        {
            services.RemoveAll<IPaymentGateway>();services.AddSingleton<IPaymentGateway,UnknownGateway>();
            services.RemoveAll<TimeProvider>();services.AddSingleton<TimeProvider>(new FutureClock());
        });
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Authorization=new("Bearer",admin.Token);
        var product=(await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        using var created=await client.PostAsJsonAsync("api/orders",new CreateOrderRequest{TotalAmount=product.Price,Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}]});created.EnsureSuccessStatusCode();
        var order=(await created.Content.ReadFromJsonAsync<OrderResponse>())!;
        client.DefaultRequestHeaders.Add("Idempotency-Key","operations-"+Guid.NewGuid());
        using var charged=await client.PostAsJsonAsync("api/payments",new CreatePaymentRequest{OrderId=order.Id,Amount=order.TotalAmount});charged.EnsureSuccessStatusCode();var payment=(await charged.Content.ReadFromJsonAsync<PaymentResponse>())!;
        DateTime? initial;
        await using(var scope=fixture.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();initial=(await db.Payments.FindAsync(payment.Id))!.PendingSince;Assert.NotNull(initial);
            db.AuditEvents.Add(new(){CorrelationId=Guid.NewGuid(),Action="ORDER_CREATE",ResourceType="ORDER",CreatedAt=DateTime.UtcNow.AddMinutes(-20)});await db.SaveChangesAsync();
        }
        for(var i=0;i<2;i++){using var reconciled=await client.PostAsync($"api/payments/{payment.Id}/reconcile",null);reconciled.EnsureSuccessStatusCode();}
        await using(var scope=fixture.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var stored=(await db.Payments.FindAsync(payment.Id))!;Assert.Equal(initial,stored.PendingSince);
            // A raw update of last lookup must also preserve pending age via DB trigger.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Payments\" SET \"PendingSince\" = {DateTime.UtcNow}, \"UpdatedAt\" = {DateTime.UtcNow} WHERE \"Id\" = {payment.Id}");
            db.ChangeTracker.Clear();Assert.Equal(initial,(await db.Payments.FindAsync(payment.Id))!.PendingSince);
        }
        using var details=await client.GetAsync("api/operations/status");details.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("stuckCount").GetInt32()>0);
        var alerts=doc.RootElement.GetProperty("alerts").EnumerateArray().Select(x=>x.GetString()).ToArray();
        Assert.Contains("PAYMENT_STUCK",alerts);Assert.Contains("AUDIT_RESULT_MISSING",alerts);Assert.Contains("RECOVERY_DISABLED_WITH_PENDING",alerts);
    }

    [DockerFact]
    public async Task AuditAppendFailureIsCountedWithoutExposingExceptionOrSecrets()
    {
        var user=await Account();var admin=await Promote(user);using var client=fixture.CreateClient();client.DefaultRequestHeaders.Authorization=new("Bearer",admin.Token);
        var product=(await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        await using var scope=fixture.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ddl="CREATE FUNCTION ops_test_fail_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'PRIVATE_EXCEPTION_SENTINEL'; END; $$; CREATE TRIGGER ops_test_failure BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW WHEN (NEW.\"ActorUserId\" = "+user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)+" AND NEW.\"Phase\" = 'BEGIN') EXECUTE FUNCTION ops_test_fail_audit();";
        await db.Database.ExecuteSqlRawAsync(ddl);
        try
        {
            using var rejected=await client.PostAsJsonAsync("api/orders",new CreateOrderRequest{TotalAmount=product.Price,Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}]});Assert.Equal(HttpStatusCode.ServiceUnavailable,rejected.StatusCode);
            using var details=await client.GetAsync("api/operations/status");details.EnsureSuccessStatusCode();var text=await details.Content.ReadAsStringAsync();using var doc=JsonDocument.Parse(text);
            Assert.True(doc.RootElement.GetProperty("process").GetProperty("audit_append_failures").GetInt64()>0);Assert.Contains("AUDIT_APPEND_FAILURE_SEEN",text);Assert.DoesNotContain("PRIVATE_EXCEPTION_SENTINEL",text);
            Assert.False(await db.Orders.AnyAsync(o=>o.CustomerId==user.Id));
        }
        finally{await db.Database.ExecuteSqlRawAsync("DROP TRIGGER ops_test_failure ON \"AuditEvents\"; DROP FUNCTION ops_test_fail_audit();");}
    }

    private sealed class FutureClock:TimeProvider{public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddMinutes(10);}
    private sealed class UnknownGateway:IPaymentGateway
    {
        public Task<GatewayResult> ChargeAsync(GatewayPaymentRequest r,CancellationToken t=default)=>Task.FromResult(new GatewayResult(GatewayOutcome.Unknown));
        public Task<GatewayResult> RefundAsync(GatewayPaymentRequest r,CancellationToken t=default)=>ChargeAsync(r,t);
        public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest r,CancellationToken t=default)=>ChargeAsync(r,t);
        public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest r,CancellationToken t=default)=>ChargeAsync(r,t);
    }
    private async Task<LoginResponse> Account()
    {
        using var client=fixture.CreateClient();var email=$"ops-{Guid.NewGuid():N}@example.test";
        using var registered=await client.PostAsJsonAsync("api/users/register",new RegisterRequest{Email=email,Password="OperationsTestPassword!123",FullName="Operations Tests"});registered.EnsureSuccessStatusCode();return await Login(email);
    }
    private async Task<LoginResponse> Promote(LoginResponse user)
    {await using var scope=fixture.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();(await db.Users.FindAsync(user.Id))!.Role="Admin";await db.SaveChangesAsync();return await Login(user.Email);}
    private async Task<LoginResponse> Login(string email)
    {using var client=fixture.CreateClient();using var r=await client.PostAsJsonAsync("api/users/login",new LoginRequest{Email=email,Password="OperationsTestPassword!123"});r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<LoginResponse>())!;}
}
