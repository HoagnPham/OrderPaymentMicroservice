using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Cart;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.DTOs.Users;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class OrderPaymentApiTests(PostgresApiFixture fixture)
{
    private const string Password = "StrongPassword!123";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [DockerFact]
    public async Task Customer_can_complete_register_cart_order_payment_flow()
    {
        var session = await RegisterAndLoginAsync("happy");
        using var client = AuthorizedClient(session.Token);
        var product = await GetFirstProductAsync(client);

        var cartResponse = await client.PostAsJsonAsync($"api/cart/user/{session.UserId}/add", new AddToCartRequest
        {
            ProductId = product.Id,
            Quantity = 2
        });
        Assert.Equal(HttpStatusCode.OK, cartResponse.StatusCode);

        var order = await CreateOrderAsync(client, product, 2);
        Assert.Equal("PAYMENT_PENDING", order.Status);
        Assert.Equal("RESERVED", order.InventoryStatus);

        var idempotencyKey = $"charge-{Guid.NewGuid():N}";
        using var paymentRequest = new HttpRequestMessage(HttpMethod.Post, "api/payments")
        {
            Content = JsonContent.Create(new CreatePaymentRequest
            {
                OrderId = order.Id,
                Amount = order.TotalAmount
            })
        };
        paymentRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);

        var payment = await ReadAsync<PaymentResponse>(paymentResponse);
        Assert.Equal("SUCCESS", payment.Status);
        Assert.False(string.IsNullOrWhiteSpace(payment.TransactionCode));

        using var repeatedRequest = new HttpRequestMessage(HttpMethod.Post, "api/payments")
        {
            Content = JsonContent.Create(new CreatePaymentRequest
            {
                OrderId = order.Id,
                Amount = order.TotalAmount
            })
        };
        repeatedRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var repeatedResponse = await client.SendAsync(repeatedRequest);
        Assert.Equal(HttpStatusCode.Created, repeatedResponse.StatusCode);
        Assert.Equal(payment.Id, (await ReadAsync<PaymentResponse>(repeatedResponse)).Id);

        var mine = await client.GetFromJsonAsync<List<OrderResponse>>("api/orders/mine", JsonOptions);
        var paidOrder = Assert.Single(mine!, candidate => candidate.Id == order.Id);
        Assert.Equal("PAID", paidOrder.Status);
        Assert.Equal("RESERVED", paidOrder.InventoryStatus);
    }

    [DockerFact]
    public async Task Authentication_and_resource_ownership_are_enforced()
    {
        var first = await RegisterAndLoginAsync("owner-a");
        var second = await RegisterAndLoginAsync("owner-b");

        using var anonymous = fixture.CreateClient();
        var anonymousResponse = await anonymous.GetAsync($"api/cart/user/{first.UserId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var secondClient = AuthorizedClient(second.Token);
        var forbiddenResponse = await secondClient.GetAsync($"api/cart/user/{first.UserId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var firstClient = AuthorizedClient(first.Token);
        var product = await GetFirstProductAsync(firstClient);
        var order = await CreateOrderAsync(firstClient, product, 1);
        var forbiddenOrder = await secondClient.GetAsync($"api/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenOrder.StatusCode);
    }

    [DockerFact]
    public async Task Concurrent_payment_requests_create_only_one_charge_record()
    {
        var session = await RegisterAndLoginAsync("payment-race");
        using var client = AuthorizedClient(session.Token);
        var product = await GetFirstProductAsync(client);
        var order = await CreateOrderAsync(client, product, 1);

        Task<HttpResponseMessage> SendPaymentAsync(string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/payments")
            {
                Content = JsonContent.Create(new CreatePaymentRequest
                {
                    OrderId = order.Id,
                    Amount = order.TotalAmount
                })
            };
            request.Headers.Add("Idempotency-Key", key);
            return client.SendAsync(request);
        }

        var responses = await Task.WhenAll(
            SendPaymentAsync($"race-a-{Guid.NewGuid():N}"),
            SendPaymentAsync($"race-b-{Guid.NewGuid():N}"));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payments = await db.Payments.AsNoTracking().Where(payment => payment.OrderId == order.Id).ToListAsync();
        Assert.Single(payments);
        Assert.Equal(PaymentStatus.SUCCESS, payments[0].Status);
    }

    [DockerFact]
    public async Task Concurrent_payment_and_cancellation_end_in_one_consistent_outcome()
    {
        var session = await RegisterAndLoginAsync("payment-cancel-race");
        using var client = AuthorizedClient(session.Token);
        var product = await GetFirstProductAsync(client);
        var order = await CreateOrderAsync(client, product, 1);

        using var paymentRequest = new HttpRequestMessage(HttpMethod.Post, "api/payments")
        {
            Content = JsonContent.Create(new CreatePaymentRequest
            {
                OrderId = order.Id,
                Amount = order.TotalAmount
            })
        };
        paymentRequest.Headers.Add("Idempotency-Key", $"payment-cancel-{Guid.NewGuid():N}");

        var paymentTask = client.SendAsync(paymentRequest);
        var cancellationTask = client.PostAsync($"api/orders/{order.Id}/cancel", null);
        await Task.WhenAll(paymentTask, cancellationTask);
        var paymentResponse = await paymentTask;
        var cancellationResponse = await cancellationTask;

        var paymentWon = paymentResponse.StatusCode == HttpStatusCode.Created;
        var cancellationWon = cancellationResponse.StatusCode == HttpStatusCode.OK;
        Assert.True(paymentWon ^ cancellationWon,
            $"Exactly one operation must win; payment={paymentResponse.StatusCode}, cancel={cancellationResponse.StatusCode}");
        Assert.Contains(
            paymentWon ? cancellationResponse.StatusCode : paymentResponse.StatusCode,
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedOrder = await db.Orders.AsNoTracking()
            .Include(candidate => candidate.Payments)
            .SingleAsync(candidate => candidate.Id == order.Id);

        if (paymentWon)
        {
            Assert.Equal(OrderStatus.PAID, persistedOrder.Status);
            Assert.Equal(InventoryReservationStatus.RESERVED, persistedOrder.InventoryStatus);
            Assert.Single(persistedOrder.Payments);
            Assert.Equal(PaymentStatus.SUCCESS, persistedOrder.Payments.Single().Status);
        }
        else
        {
            Assert.Equal(OrderStatus.CANCELLED, persistedOrder.Status);
            Assert.Equal(InventoryReservationStatus.RELEASED, persistedOrder.InventoryStatus);
            Assert.Empty(persistedOrder.Payments);
        }
    }

    [DockerFact]
    public async Task Admin_can_confirm_and_complete_paid_order()
    {
        var customer = await RegisterAndLoginAsync("admin-flow-customer");
        using var customerClient = AuthorizedClient(customer.Token);
        var product = await GetFirstProductAsync(customerClient);
        var order = await CreateOrderAsync(customerClient, product, 1);
        var payment = await CreateSuccessfulPaymentAsync(customerClient, order);
        Assert.Equal("SUCCESS", payment.Status);

        var admin = await RegisterAndLoginAsync("admin-flow-admin");
        await PromoteToAdminAsync(admin.UserId);
        var adminLogin = await LoginAsync(admin.Email);
        using var adminClient = AuthorizedClient(adminLogin.Token);

        var confirmResponse = await adminClient.PostAsync($"api/orders/{order.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmed = await ReadAsync<OrderResponse>(confirmResponse);
        Assert.Equal("CONFIRMED", confirmed.Status);

        var completeResponse = await adminClient.PostAsync($"api/orders/{order.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completed = await ReadAsync<OrderResponse>(completeResponse);
        Assert.Equal("COMPLETED", completed.Status);
        Assert.Equal("CONSUMED", completed.InventoryStatus);
    }

    private async Task<(int UserId, string Email, string Token)> RegisterAndLoginAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.test";
        using var client = fixture.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("api/users/register", new RegisterRequest
        {
            Email = email,
            Password = Password,
            FullName = "Integration Test User"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var login = await LoginAsync(email);
        return (login.Id, email, login.Token);
    }

    private async Task<LoginResponse> LoginAsync(string email)
    {
        using var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("api/users/login", new LoginRequest
        {
            Email = email,
            Password = Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<LoginResponse>(response);
    }

    private HttpClient AuthorizedClient(string token)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ProductResponse> GetFirstProductAsync(HttpClient client)
    {
        var products = await client.GetFromJsonAsync<List<ProductResponse>>("api/products", JsonOptions);
        return Assert.Single(products!.Take(1));
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, ProductResponse product, int quantity)
    {
        var response = await client.PostAsJsonAsync("api/orders", new CreateOrderRequest
        {
            TotalAmount = product.Price * quantity,
            Address = "1 Integration Street",
            City = "Test City",
            Items =
            [
                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = quantity,
                    UnitPrice = product.Price
                }
            ]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<OrderResponse>(response);
    }

    private static async Task<PaymentResponse> CreateSuccessfulPaymentAsync(HttpClient client, OrderResponse order)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/payments")
        {
            Content = JsonContent.Create(new CreatePaymentRequest { OrderId = order.Id, Amount = order.TotalAmount })
        };
        request.Headers.Add("Idempotency-Key", $"charge-{Guid.NewGuid():N}");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<PaymentResponse>(response);
    }

    private async Task PromoteToAdminAsync(int userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
        user.Role = "Admin";
        await db.SaveChangesAsync();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions)
        ?? throw new InvalidOperationException($"Response did not contain {typeof(T).Name}.");
}
