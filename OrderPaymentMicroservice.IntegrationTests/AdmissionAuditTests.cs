using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderPaymentMicroservice.Auditing;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class AdmissionAuditTests(PostgresApiFixture fixture)
{
    [DockerFact]
    public async Task RateLimitsUseSeparateAuthBucketAndSeparateVerifiedUserBuckets()
    {
        var a = await Account(); var b = await Account();
        await using var factory = fixture.CreateIsolatedFactory(new() { ["ApiAdmission:AuthenticationPermitLimit"] = "1", ["ApiAdmission:ApiPermitLimit"] = "1", ["ApiAdmission:WindowSeconds"] = "60" });
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/users/login", new LoginRequest { Email = "missing@example.test", Password = "wrongpassword" })).StatusCode);
        var limited = await anonymous.PostAsJsonAsync("api/users/login", new LoginRequest { Email = "missing@example.test", Password = "wrongpassword" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.True(limited.Headers.Contains("Retry-After"));
        using var first = factory.CreateClient(); first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);
        using var second = factory.CreateClient(); second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", b.Token);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("api/orders/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.GetAsync("api/orders/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("api/orders/mine")).StatusCode);
    }

    [DockerFact]
    public async Task BodyAndItemLimitsRejectBeforeOrderPersistence()
    {
        var user = await Account();
        await using var factory = fixture.CreateIsolatedFactory(new() { ["ApiAdmission:MaxBodyBytes"] = "1024", ["ApiAdmission:MaxOrderItems"] = "2" });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("api/orders", new StringContent(new string('A',1025), Encoding.UTF8, "application/json"))).StatusCode);
        var product = (await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        var rejected = await client.PostAsJsonAsync("api/orders", new CreateOrderRequest
        { TotalAmount = product.Price * 3, Items = Enumerable.Range(0,3).Select(_ => new CreateOrderItemRequest { ProductId = product.Id, Quantity = 1, UnitPrice = product.Price }).ToList() });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Orders.AnyAsync(o => o.CustomerId == user.Id));
    }

    [DockerFact]
    public async Task PaymentCancelRefundAuditPersistsWithoutSecretsAndRejectsMutation()
    {
        var user = await Account(); using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", user.Token);
        var product = (await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        async Task<OrderResponse> Order()
        {
            var r = await client.PostAsJsonAsync("api/orders", new CreateOrderRequest { TotalAmount = product.Price, Items = [new() { ProductId = product.Id, Quantity = 1, UnitPrice = product.Price }] });
            r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<OrderResponse>())!;
        }
        var cancelled = await Order(); Assert.Equal(HttpStatusCode.OK,(await client.PostAsync($"api/orders/{cancelled.Id}/cancel",null)).StatusCode);
        var order = await Order(); var key = "SECRET-KEY-" + Guid.NewGuid();
        using var paymentRequest = new HttpRequestMessage(HttpMethod.Post,"api/payments") { Content = JsonContent.Create(new CreatePaymentRequest { OrderId=order.Id, Amount=order.TotalAmount }) };
        paymentRequest.Headers.Add("Idempotency-Key",key);
        var paid = await client.SendAsync(paymentRequest); paid.EnsureSuccessStatusCode();
        var payment = (await paid.Content.ReadFromJsonAsync<PaymentResponse>())!;
        var correlation = Guid.Parse(paid.Headers.GetValues("X-Correlation-Id").Single());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var stored = await db.Users.FindAsync(user.Id); stored!.Role="Admin"; await db.SaveChangesAsync();
        }
        var adminLogin = await Login(user.Email); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",adminLogin.Token);
        using var refund = new HttpRequestMessage(HttpMethod.Post,$"api/payments/{payment.Id}/refund"); refund.Headers.Add("Idempotency-Key","refund-"+key);
        Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(refund)).StatusCode);
        await using var check = fixture.Services.CreateAsyncScope(); var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await context.AuditEvents.Where(e=>e.ActorUserId==user.Id).ToListAsync();
        Assert.Contains(entries,e=>e.Action=="ORDER_CANCEL" && e.Phase=="RESULT" && e.Outcome=="HTTP_SUCCESS");
        Assert.Contains(entries,e=>e.Action=="PAYMENT_REFUND" && e.Phase=="RESULT" && e.ResourceId==payment.Id);
        Assert.Equal(2,entries.Count(e=>e.CorrelationId==correlation && e.Phase != "COMMIT"));
        Assert.Contains(entries,e=>e.CorrelationId==correlation && e.Phase=="COMMIT" && e.ResourceId==payment.Id && e.ResourceType=="PAYMENT");
        var json=JsonSerializer.Serialize(entries); Assert.DoesNotContain(key,json); Assert.DoesNotContain(user.Token,json); Assert.DoesNotContain("AuditPassword!123",json);
        var id=entries[0].Id;
        var update=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AuditEvents\" SET \"Outcome\" = {'X'.ToString()} WHERE \"Id\" = {id}"));
        Assert.Equal("P0001",update.SqlState);
        Assert.Equal("P0001",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AuditEvents\" WHERE \"Id\" = {id}"))).SqlState);
        Assert.Equal("P0001",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>context.Database.ExecuteSqlRawAsync("TRUNCATE \"AuditEvents\""))).SqlState);
    }

    [DockerFact]
    public async Task MissingAuditStorePreventsBusinessOperation()
    {
        var user=await Account();
        await using var factory=fixture.CreateIsolatedFactory(new(),services=> { services.RemoveAll<IAuditWriter>(); services.AddSingleton<IAuditWriter,UnavailableAuditWriter>(); });
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",user.Token);
        var product=(await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        var response=await client.PostAsJsonAsync("api/orders",new CreateOrderRequest { TotalAmount=product.Price,Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}] });
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        await using var scope=fixture.Services.CreateAsyncScope();Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AnyAsync(o=>o.CustomerId==user.Id));
    }

    [DockerFact]
    public async Task MissingAuditResultReturns503WithDurableBeginAndCommittedOrder()
    {
        var user = await Account();
        await using var factory = fixture.CreateIsolatedFactory(new(), services =>
        {
            services.RemoveAll<IAuditWriter>();
            services.AddSingleton<IAuditWriter>(provider => new ResultFailureAuditWriter(new AuditWriter(provider.GetRequiredService<IServiceScopeFactory>())));
        });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        var product = (await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        var request = new CreateOrderRequest { TotalAmount=product.Price, Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}] };
        var key = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("Idempotency-Key",key);
        var result = await client.PostAsJsonAsync("api/orders",request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        var correlation=Guid.Parse(result.Headers.GetValues("X-Correlation-Id").Single());
        await using var scope=fixture.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,await db.Orders.CountAsync(o=>o.CustomerId==user.Id));
        Assert.Equal("BEGIN",(await db.AuditEvents.SingleAsync(e=>e.CorrelationId==correlation && e.Phase != "COMMIT")).Phase);
        Assert.Contains(await db.AuditEvents.Where(e=>e.CorrelationId==correlation).ToListAsync(),e=>e.Phase=="COMMIT" && e.ResourceId.HasValue);
        using var retry = fixture.CreateClient(); retry.DefaultRequestHeaders.Authorization = new("Bearer",user.Token);
        retry.DefaultRequestHeaders.Add("Idempotency-Key",key);
        var replay = await retry.PostAsJsonAsync("api/orders",request); Assert.Equal(HttpStatusCode.Created,replay.StatusCode);
        Assert.Equal((await db.Orders.SingleAsync(o=>o.CustomerId==user.Id)).Id,(await replay.Content.ReadFromJsonAsync<OrderResponse>())!.Id);
        Assert.Equal(1,await db.Orders.CountAsync(o=>o.CustomerId==user.Id));
    }

    [DockerFact]
    public async Task StreamingBodyWithoutLengthIsRejectedBeforeJsonBinding()
    {
        await using var factory=fixture.CreateIsolatedFactory(new(){["ApiAdmission:MaxBodyBytes"]="1024"});
        using var client=factory.CreateClient();
        var result=await client.PostAsync("api/orders",new UnknownLengthContent());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,result.StatusCode);
    }

    private sealed class ResultFailureAuditWriter(IAuditWriter inner):IAuditWriter
    {
        public Task AppendAsync(AuditEvent e)=>e.Phase=="RESULT" ? throw new IOException("Result unavailable") : inner.AppendAsync(e);
    }
    private sealed class UnknownLengthContent:HttpContent
    {
        protected override bool TryComputeLength(out long length) { length=0;return false; }
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(new byte[1025]).AsTask();
    }

    private sealed class UnavailableAuditWriter:IAuditWriter { public Task AppendAsync(AuditEvent e)=>throw new IOException("Unavailable"); }
    private async Task<LoginResponse> Account()
    {
        var email=$"audit-{Guid.NewGuid():N}@example.test";using var client=fixture.CreateClient();
        var r=await client.PostAsJsonAsync("api/users/register",new RegisterRequest { Email=email,Password="AuditPassword!123",FullName="Audit Test" });r.EnsureSuccessStatusCode();return await Login(email);
    }
    private async Task<LoginResponse> Login(string email)
    {
        using var client=fixture.CreateClient();var r=await client.PostAsJsonAsync("api/users/login",new LoginRequest { Email=email,Password="AuditPassword!123" });r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
