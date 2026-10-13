using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class AtomicAuditIdempotencyTests(PostgresApiFixture fixture)
{
    [DockerFact]
    public async Task ReplayFromAnotherHostReturnsOriginalReceiptEvenAfterCancellation()
    {
        var user = await Account(); using var client = Client(user);
        var request = await Request(client); var key = Guid.NewGuid().ToString();
        var first = await Create(client, request, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var receipt = await first.Content.ReadAsStringAsync(); var order = (await first.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"api/orders/{order.Id}/cancel", null)).StatusCode);
        await using var anotherHost = fixture.CreateIsolatedFactory(new());
        using var replayClient = anotherHost.CreateClient(); replayClient.DefaultRequestHeaders.Authorization = new("Bearer", user.Token);
        var replay = await Create(replayClient, request, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.CustomerId == user.Id));
        var correlation = Guid.Parse(first.Headers.GetValues("X-Correlation-Id").Single());
        Assert.True(await db.AuditEvents.AnyAsync(e => e.CorrelationId == correlation && e.Phase == "COMMIT" && e.ResourceId == order.Id));
        Assert.Equal("CANCELLED", (await client.GetFromJsonAsync<OrderResponse>($"api/orders/{order.Id}"))!.Status);
    }

    [DockerFact]
    public async Task KeyIsBoundToPayloadAndScopedToAuthenticatedUser()
    {
        var first = await Account(); var second = await Account(); using var a = Client(first); using var b = Client(second);
        var request = await Request(a); var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Created, (await Create(a, request, key)).StatusCode);
        request.City = "changed-city";
        Assert.Equal(HttpStatusCode.Conflict, (await Create(a, request, key)).StatusCode);
        // A spoofed CustomerId cannot take over another principal's key namespace.
        request.CustomerId = first.Id;
        Assert.Equal(HttpStatusCode.Created, (await Create(b, request, key)).StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.CustomerId == first.Id));
        Assert.Equal(1, await db.Orders.CountAsync(o => o.CustomerId == second.Id));
    }

    [DockerFact]
    public async Task ConcurrentSameKeyReservesInventoryOnlyOnceAndCanBeRetried()
    {
        var user = await Account(); using var client = Client(user); var request = await Request(client);
        var productId = request.Items[0].ProductId;
        var before = await Stock(productId); var key = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create(client, request, key)));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        var replay = await Create(client, request, key); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var id = (await replay.Content.ReadFromJsonAsync<OrderResponse>())!.Id;
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
            Assert.Equal(id, (await response.Content.ReadFromJsonAsync<OrderResponse>())!.Id);
        Assert.Equal(before - 1, await Stock(productId));
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.CountAsync(o => o.CustomerId == user.Id));
    }

    [DockerFact]
    public async Task InvalidAndMultipleKeysAreRejectedWithoutCreatingOrders()
    {
        var user = await Account(); using var client = Client(user); var request = await Request(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(client, request, new string('K',129))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(client, request, "contains space")).StatusCode);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/orders") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", new[] { "first", "second" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(message)).StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AnyAsync(o => o.CustomerId == user.Id));
    }

    [DockerFact]
    public async Task AtomicAuditInsertFailureRollsBackOrderAndInventory()
    {
        var user = await Account(); using var client = Client(user); var request = await Request(client);
        var before = await Stock(request.Items[0].ProductId);
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Inject only a COMMIT insert failure for this actor; BEGIN/RESULT still work.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION test_fail_commit_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected audit failure'; END; $$;
            """);
        // DDL WHEN expressions cannot use SQL parameters; this value is a generated integer, never request text.
        var failureTrigger = "CREATE TRIGGER test_commit_failure BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW WHEN (NEW.\"Phase\" = 'COMMIT' AND NEW.\"ActorUserId\" = "
            + user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") EXECUTE FUNCTION test_fail_commit_audit();";
        await db.Database.ExecuteSqlRawAsync(failureTrigger);
        try
        {
            var result = await Create(client, request, Guid.NewGuid().ToString());
            Assert.False(result.IsSuccessStatusCode);
            Assert.False(await db.Orders.AnyAsync(o => o.CustomerId == user.Id));
            Assert.False(await db.AuditEvents.AnyAsync(e => e.ActorUserId == user.Id && e.Phase == "COMMIT"));
            Assert.Equal(before, await Stock(request.Items[0].ProductId));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_commit_failure ON \"AuditEvents\"; DROP FUNCTION test_fail_commit_audit();");
        }
    }

    [DockerFact]
    public async Task DirectSqlMutationIsAuditedAndTransactionRollbackRemovesItsAudit()
    {
        var user = await Account(); using var client = Client(user); var request = await Request(client);
        var response = await Create(client, request, Guid.NewGuid().ToString()); response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<OrderResponse>())!.Id;
        await using var scope = fixture.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var baseline = await db.AuditEvents.CountAsync(e => e.ResourceType == "ORDER" && e.ResourceId == id && e.Phase == "COMMIT");
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Orders\" SET \"City\" = {'R'.ToString()} WHERE \"Id\" = {id}");
            Assert.Equal(baseline + 1, await db.AuditEvents.CountAsync(e => e.ResourceType == "ORDER" && e.ResourceId == id && e.Phase == "COMMIT"));
            await tx.RollbackAsync();
        }
        Assert.Equal(baseline, await db.AuditEvents.CountAsync(e => e.ResourceType == "ORDER" && e.ResourceId == id && e.Phase == "COMMIT"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Orders\" SET \"City\" = {'C'.ToString()} WHERE \"Id\" = {id}");
        Assert.True(await db.AuditEvents.AnyAsync(e => e.ResourceType == "ORDER" && e.ResourceId == id && e.Phase == "COMMIT" && e.ActorKind == "SYSTEM" && e.ActorUserId == null));
    }

    private HttpClient Client(LoginResponse user)
    { var client = fixture.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",user.Token); return client; }
    private static Task<HttpResponseMessage> Create(HttpClient client, CreateOrderRequest request, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post,"api/orders") { Content=JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key",key); return client.SendAsync(message);
    }
    private async Task<int> Stock(int id)
    { await using var scope = fixture.Services.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.AsNoTracking().SingleAsync(p=>p.Id==id)).Stock; }
    private static async Task<CreateOrderRequest> Request(HttpClient client)
    {
        var product = (await client.GetFromJsonAsync<List<ProductResponse>>("api/products"))!.First();
        return new() { TotalAmount=product.Price,Address="Test address",City="Test city", Items=[new(){ProductId=product.Id,Quantity=1,UnitPrice=product.Price}] };
    }
    private async Task<LoginResponse> Account()
    {
        using var client=fixture.CreateClient();var email=$"atomic-{Guid.NewGuid():N}@example.test";
        var registered=await client.PostAsJsonAsync("api/users/register",new RegisterRequest { Email=email,Password="AtomicPassword!123",FullName="Atomic Test" });registered.EnsureSuccessStatusCode();
        var login=await client.PostAsJsonAsync("api/users/login",new LoginRequest { Email=email,Password="AtomicPassword!123" });login.EnsureSuccessStatusCode();return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
