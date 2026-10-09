using System.Text.Json;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

/// <summary>
/// Deterministic in-process fuzz campaigns over the real validators and state
/// machines. ArgumentException is a normal rejected input; any other exception
/// or accepted invariant violation is a campaign failure.
/// </summary>
public sealed class SecurityFuzzingTests
{
    private const int Seed = 20261009;
    private const int Iterations = 25_000;

    [Fact]
    public void SeedCorpusMatchesExpectedValidatorOutcome()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "FuzzCorpus", "order_requests.json");
        var corpus = JsonSerializer.Deserialize<List<CorpusOrderRequest>>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var validator = new InputValidator();

        foreach (var entry in corpus)
        {
            var request = entry.ToRequest();
            var exception = Record.Exception(() => validator.ValidateCreateOrderRequest(request));
            if (entry.ExpectedValid)
                Assert.Null(exception);
            else
                Assert.IsAssignableFrom<ArgumentException>(exception);
        }
    }

    [Fact]
    public void SeededOrderAndPaymentValidatorCampaignHasNoUnexpectedCrash()
    {
        var random = new Random(Seed);
        var orderValidator = new InputValidator();
        var paymentValidator = new PaymentValidator();

        Assert.Throws<ArgumentNullException>(() => orderValidator.ValidateCreateOrderRequest(null!));
        Assert.Throws<ArgumentException>(() => orderValidator.ValidateCreateOrderRequest(new CreateOrderRequest
        {
            CustomerId = 1,
            TotalAmount = 1m,
            Items = null!
        }));
        Assert.Throws<ArgumentNullException>(() => orderValidator.ValidateCreateOrderRequest(new CreateOrderRequest
        {
            CustomerId = 1,
            TotalAmount = 1m,
            Items = [null!]
        }));
        Assert.Throws<ArgumentNullException>(() => paymentValidator.ValidateCreatePaymentRequest(null!));

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var orderRequest = NextOrderRequest(random);
            try
            {
                orderValidator.ValidateCreateOrderRequest(orderRequest);
                AssertAcceptedOrderInvariant(orderRequest);
            }
            catch (ArgumentException)
            {
                // Invalid input is an expected, controlled outcome.
            }

            var paymentRequest = new CreatePaymentRequest
            {
                OrderId = random.Next(-2, 5),
                Amount = NextMoney(random),
                IdempotencyKey = NextIdempotencyKey(random)
            };
            try
            {
                paymentValidator.ValidateCreatePaymentRequest(paymentRequest);
                Assert.True(paymentRequest.OrderId > 0);
                Assert.InRange(paymentRequest.Amount, 0.01m, 999999.99m);
                Assert.False(string.IsNullOrWhiteSpace(paymentRequest.IdempotencyKey));
                Assert.InRange(paymentRequest.IdempotencyKey.Length, 1, 128);
            }
            catch (ArgumentException)
            {
                // Invalid input is an expected, controlled outcome.
            }
        }
    }

    [Fact]
    public void SeededStateMachineCampaignMatchesIndependentTransitionTables()
    {
        var random = new Random(Seed);
        var orderTransitions = new HashSet<(OrderStatus From, OrderStatus To)>
        {
            (OrderStatus.CREATED, OrderStatus.VALIDATED),
            (OrderStatus.VALIDATED, OrderStatus.PAYMENT_PENDING),
            (OrderStatus.PAYMENT_PENDING, OrderStatus.PAID),
            (OrderStatus.PAID, OrderStatus.CONFIRMED),
            (OrderStatus.CONFIRMED, OrderStatus.COMPLETED),
            (OrderStatus.CREATED, OrderStatus.CANCELLED),
            (OrderStatus.VALIDATED, OrderStatus.CANCELLED),
            (OrderStatus.PAYMENT_PENDING, OrderStatus.CANCELLED),
            (OrderStatus.PAYMENT_PENDING, OrderStatus.FAILED),
            (OrderStatus.PAID, OrderStatus.REFUND_PENDING),
            (OrderStatus.CONFIRMED, OrderStatus.REFUND_PENDING),
            (OrderStatus.REFUND_PENDING, OrderStatus.REFUNDED)
        };
        var paymentTransitions = new HashSet<(PaymentStatus From, PaymentStatus To)>
        {
            (PaymentStatus.PENDING, PaymentStatus.PROCESSING),
            (PaymentStatus.PROCESSING, PaymentStatus.SUCCESS),
            (PaymentStatus.PROCESSING, PaymentStatus.FAILED),
            (PaymentStatus.PROCESSING, PaymentStatus.UNKNOWN),
            (PaymentStatus.UNKNOWN, PaymentStatus.PROCESSING),
            (PaymentStatus.UNKNOWN, PaymentStatus.FAILED),
            (PaymentStatus.SUCCESS, PaymentStatus.REFUND_PENDING),
            (PaymentStatus.REFUND_PENDING, PaymentStatus.REFUNDED)
        };
        var orderStates = Enum.GetValues<OrderStatus>();
        var paymentStates = Enum.GetValues<PaymentStatus>();

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var orderFrom = orderStates[random.Next(orderStates.Length)];
            var orderTo = orderStates[random.Next(orderStates.Length)];
            Assert.Equal(orderTransitions.Contains((orderFrom, orderTo)), OrderStateMachine.CanTransition(orderFrom, orderTo));
            Assert.Equal(
                orderFrom is OrderStatus.COMPLETED or OrderStatus.CANCELLED or OrderStatus.FAILED or OrderStatus.REFUNDED,
                OrderStateMachine.IsTerminal(orderFrom));

            var order = new Order { Status = orderFrom };
            if (orderTransitions.Contains((orderFrom, orderTo)))
            {
                OrderStateMachine.Transition(order, orderTo);
                Assert.Equal(orderTo, order.Status);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => OrderStateMachine.Transition(order, orderTo));
                Assert.Equal(orderFrom, order.Status);
            }

            var paymentFrom = paymentStates[random.Next(paymentStates.Length)];
            var paymentTo = paymentStates[random.Next(paymentStates.Length)];
            Assert.Equal(paymentTransitions.Contains((paymentFrom, paymentTo)), PaymentStateMachine.CanTransition(paymentFrom, paymentTo));
            Assert.Equal(
                paymentFrom is PaymentStatus.FAILED or PaymentStatus.REFUNDED,
                PaymentStateMachine.IsTerminal(paymentFrom));

            var payment = new Payment { Status = paymentFrom };
            if (paymentTransitions.Contains((paymentFrom, paymentTo)))
            {
                PaymentStateMachine.Transition(payment, paymentTo);
                Assert.Equal(paymentTo, payment.Status);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => PaymentStateMachine.Transition(payment, paymentTo));
                Assert.Equal(paymentFrom, payment.Status);
            }
        }
    }

    private static CreateOrderRequest NextOrderRequest(Random random)
    {
        var itemCount = random.Next(0, 5);
        var items = new List<CreateOrderItemRequest>();
        decimal calculated = 0;
        for (var index = 0; index < itemCount; index++)
        {
            var item = new CreateOrderItemRequest
            {
                ProductId = random.Next(-2, 5),
                Quantity = random.Next(-2, 10_005),
                UnitPrice = NextMoney(random)
            };
            items.Add(item);
            calculated += item.Quantity * item.UnitPrice;
        }

        var total = random.Next(4) == 0 ? calculated : NextMoney(random);
        return new CreateOrderRequest
        {
            CustomerId = random.Next(-2, 5),
            TotalAmount = total,
            Items = items
        };
    }

    private static decimal NextMoney(Random random) => random.Next(8) switch
    {
        0 => -0.01m,
        1 => 0m,
        2 => 0.001m,
        3 => 0.01m,
        4 => 999999.99m,
        5 => 1000000m,
        _ => random.Next(1, 1_000_000) / 100m
    };

    private static string NextIdempotencyKey(Random random) => random.Next(5) switch
    {
        0 => string.Empty,
        1 => "   ",
        2 => new string('K', 129),
        _ => $"fuzz-{random.NextInt64():x}"
    };

    private static void AssertAcceptedOrderInvariant(CreateOrderRequest request)
    {
        Assert.True(request.CustomerId > 0);
        Assert.InRange(request.TotalAmount, 0.01m, 999999.99m);
        Assert.NotEmpty(request.Items);
        var calculated = 0m;
        foreach (var item in request.Items)
        {
            Assert.True(item.ProductId > 0);
            Assert.InRange(item.Quantity, 1, 10_000);
            Assert.InRange(item.UnitPrice, 0.01m, 999999.99m);
            calculated += item.Quantity * item.UnitPrice;
        }
        Assert.InRange(Math.Abs(request.TotalAmount - calculated), 0m, 0.01m);
    }

    private sealed class CorpusOrderRequest
    {
        public string Name { get; init; } = string.Empty;
        public bool ExpectedValid { get; init; }
        public int CustomerId { get; init; }
        public decimal TotalAmount { get; init; }
        public List<CreateOrderItemRequest> Items { get; init; } = [];

        public CreateOrderRequest ToRequest() => new()
        {
            CustomerId = CustomerId,
            TotalAmount = TotalAmount,
            Items = Items
        };
    }
}
