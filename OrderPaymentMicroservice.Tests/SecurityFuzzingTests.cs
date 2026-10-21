using System.Text.Json;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using Xunit;
using Xunit.Sdk;

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
    private const int StatefulSequences = 2_000;
    private const int StatefulStepsPerSequence = 20;

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
            catch (Exception ex)
            {
                throw FuzzFailure("validator", iteration, new { orderRequest }, ex);
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
                Assert.Equal(decimal.Round(paymentRequest.Amount, 2), paymentRequest.Amount);
                Assert.False(string.IsNullOrWhiteSpace(paymentRequest.IdempotencyKey));
                Assert.InRange(paymentRequest.IdempotencyKey.Length, 1, 128);
            }
            catch (ArgumentException)
            {
                // Invalid input is an expected, controlled outcome.
            }
            catch (Exception ex)
            {
                throw FuzzFailure("payment-validator", iteration, new { paymentRequest }, ex);
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
            var paymentFrom = paymentStates[random.Next(paymentStates.Length)];
            var paymentTo = paymentStates[random.Next(paymentStates.Length)];
            try
            {
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
            catch (Exception ex)
            {
                throw FuzzFailure("state-machine-pairs", iteration,
                    new { orderFrom, orderTo, paymentFrom, paymentTo }, ex);
            }
        }
    }

    [Fact]
    public void SeededStatefulOrderPaymentSequencesRespectAggregateInvariants()
    {
        var random = new Random(Seed);
        var observedAggregateStates = new HashSet<string>(StringComparer.Ordinal);

        for (var sequence = 0; sequence < StatefulSequences; sequence++)
        {
            var aggregate = new FuzzAggregate();
            var trace = new List<string>();
            try
            {
                for (var step = 0; step < StatefulStepsPerSequence; step++)
                {
                    var operation = (FuzzOperation)random.Next(Enum.GetValues<FuzzOperation>().Length);
                    Apply(operation, aggregate, trace);
                    AssertAggregateInvariant(aggregate);
                    observedAggregateStates.Add(aggregate.Describe());
                }
            }
            catch (Exception ex)
            {
                throw FuzzFailure("stateful-order-payment", sequence,
                    new { steps_per_sequence = StatefulStepsPerSequence, trace, state = aggregate.Describe() }, ex);
            }
        }

        // The exact count is not a coverage metric.  It is a regression guard that
        // ensures the deterministic campaign keeps exploring several aggregate states.
        Assert.True(observedAggregateStates.Count >= 10,
            $"Expected at least 10 aggregate states; observed {observedAggregateStates.Count}: {string.Join(", ", observedAggregateStates.Order())}");
    }

    private static void Apply(FuzzOperation operation, FuzzAggregate aggregate, List<string> trace)
    {
        trace.Add(operation.ToString());
        var order = aggregate.Order;
        var payment = aggregate.Payment;
        switch (operation)
        {
            case FuzzOperation.Validate when order.Status == OrderStatus.CREATED:
                OrderStateMachine.Transition(order, OrderStatus.VALIDATED);
                break;
            case FuzzOperation.Persist when order.Status == OrderStatus.VALIDATED:
                OrderStateMachine.Transition(order, OrderStatus.PAYMENT_PENDING);
                break;
            case FuzzOperation.StartPayment when order.Status == OrderStatus.PAYMENT_PENDING && payment == null:
            {
                payment = new Payment { Status = PaymentStatus.PENDING, Amount = 10m, IdempotencyKey = "fuzz-key" };
                PaymentStateMachine.Transition(payment, PaymentStatus.PROCESSING);
                aggregate.Payment = payment;
                break;
            }
            case FuzzOperation.ChargeSucceeded when payment?.Status == PaymentStatus.PROCESSING:
            {
                PaymentStateMachine.Transition(payment, PaymentStatus.SUCCESS);
                OrderStateMachine.Transition(order, OrderStatus.PAID);
                break;
            }
            case FuzzOperation.ChargeUnknown when payment?.Status == PaymentStatus.PROCESSING:
                PaymentStateMachine.Transition(payment, PaymentStatus.UNKNOWN);
                break;
            case FuzzOperation.RetryPayment when payment?.Status == PaymentStatus.UNKNOWN:
                PaymentStateMachine.Transition(payment, PaymentStatus.PROCESSING);
                break;
            case FuzzOperation.ChargeDeclined when payment?.Status is PaymentStatus.PROCESSING or PaymentStatus.UNKNOWN:
                if (payment.Status == PaymentStatus.UNKNOWN)
                    PaymentStateMachine.Transition(payment, PaymentStatus.PROCESSING);
                PaymentStateMachine.Transition(payment, PaymentStatus.FAILED);
                OrderStateMachine.Transition(order, OrderStatus.FAILED);
                order.InventoryStatus = InventoryReservationStatus.RELEASED;
                break;
            case FuzzOperation.Cancel when payment == null && OrderStateMachine.CanTransition(order.Status, OrderStatus.CANCELLED):
                OrderStateMachine.Transition(order, OrderStatus.CANCELLED);
                order.InventoryStatus = InventoryReservationStatus.RELEASED;
                break;
            case FuzzOperation.Confirm when order.Status == OrderStatus.PAID && payment?.Status == PaymentStatus.SUCCESS:
                OrderStateMachine.Transition(order, OrderStatus.CONFIRMED);
                break;
            case FuzzOperation.Complete when order.Status == OrderStatus.CONFIRMED && payment?.Status == PaymentStatus.SUCCESS:
                OrderStateMachine.Transition(order, OrderStatus.COMPLETED);
                order.InventoryStatus = InventoryReservationStatus.CONSUMED;
                break;
            case FuzzOperation.StartRefund when payment?.Status == PaymentStatus.SUCCESS &&
                                               OrderStateMachine.CanTransition(order.Status, OrderStatus.REFUND_PENDING):
                PaymentStateMachine.Transition(payment, PaymentStatus.REFUND_PENDING);
                OrderStateMachine.Transition(order, OrderStatus.REFUND_PENDING);
                break;
            case FuzzOperation.RefundSucceeded when payment?.Status == PaymentStatus.REFUND_PENDING &&
                                                   order.Status == OrderStatus.REFUND_PENDING:
                PaymentStateMachine.Transition(payment, PaymentStatus.REFUNDED);
                OrderStateMachine.Transition(order, OrderStatus.REFUNDED);
                order.InventoryStatus = InventoryReservationStatus.RELEASED;
                break;
            case FuzzOperation.IllegalTransition:
                AssertIllegalTransitionPreservesState(order, payment);
                break;
        }
    }

    private static void AssertIllegalTransitionPreservesState(Order order, Payment? payment)
    {
        var originalOrder = order.Status;
        Assert.Throws<InvalidOperationException>(() => OrderStateMachine.Transition(order, OrderStatus.CREATED));
        Assert.Equal(originalOrder, order.Status);
        if (payment == null) return;
        var originalPayment = payment.Status;
        Assert.Throws<InvalidOperationException>(() => PaymentStateMachine.Transition(payment, PaymentStatus.PENDING));
        Assert.Equal(originalPayment, payment.Status);
    }

    private static void AssertAggregateInvariant(FuzzAggregate aggregate)
    {
        var order = aggregate.Order;
        var payment = aggregate.Payment;
        if (order.Status is OrderStatus.PAID or OrderStatus.CONFIRMED or OrderStatus.COMPLETED)
            Assert.Equal(PaymentStatus.SUCCESS, payment?.Status);
        if (order.Status == OrderStatus.CANCELLED)
            Assert.False(payment?.Status is PaymentStatus.PROCESSING or PaymentStatus.UNKNOWN or PaymentStatus.SUCCESS or PaymentStatus.REFUND_PENDING);
        if (order.Status == OrderStatus.FAILED)
            Assert.False(payment?.Status is PaymentStatus.SUCCESS or PaymentStatus.REFUND_PENDING or PaymentStatus.REFUNDED);
        if (payment?.Status == PaymentStatus.UNKNOWN)
        {
            Assert.Equal(OrderStatus.PAYMENT_PENDING, order.Status);
            Assert.Equal(InventoryReservationStatus.RESERVED, order.InventoryStatus);
        }
        if (payment?.Status == PaymentStatus.REFUND_PENDING)
            Assert.Equal(OrderStatus.REFUND_PENDING, order.Status);
        if (payment?.Status == PaymentStatus.REFUNDED)
        {
            Assert.Equal(OrderStatus.REFUNDED, order.Status);
            Assert.Equal(InventoryReservationStatus.RELEASED, order.InventoryStatus);
        }
        if (order.Status == OrderStatus.COMPLETED)
            Assert.Equal(InventoryReservationStatus.CONSUMED, order.InventoryStatus);
    }

    private static XunitException FuzzFailure(string campaign, int iteration, object input, Exception ex) =>
        new($"FUZZ FAILURE campaign={campaign}; seed={Seed}; iteration={iteration}; input={JsonSerializer.Serialize(input)}{Environment.NewLine}{ex}");

    private sealed class FuzzAggregate
    {
        public Order Order { get; } = new()
        {
            Status = OrderStatus.CREATED,
            InventoryStatus = InventoryReservationStatus.RESERVED
        };

        public Payment? Payment { get; set; }

        public string Describe() => $"order={Order.Status};payment={Payment?.Status.ToString() ?? "NONE"};inventory={Order.InventoryStatus}";
    }

    private enum FuzzOperation
    {
        Validate, Persist, StartPayment, ChargeSucceeded, ChargeUnknown, RetryPayment,
        ChargeDeclined, Cancel, Confirm, Complete, StartRefund, RefundSucceeded, IllegalTransition
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
        6 => 25.005m,
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
        Assert.Equal(decimal.Round(request.TotalAmount, 2), request.TotalAmount);
        Assert.NotEmpty(request.Items);
        var calculated = 0m;
        foreach (var item in request.Items)
        {
            Assert.True(item.ProductId > 0);
            Assert.InRange(item.Quantity, 1, 10_000);
            Assert.InRange(item.UnitPrice, 0.01m, 999999.99m);
            Assert.Equal(decimal.Round(item.UnitPrice, 2), item.UnitPrice);
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
