using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.CREATED, OrderStatus.VALIDATED)]
    [InlineData(OrderStatus.VALIDATED, OrderStatus.PAYMENT_PENDING)]
    [InlineData(OrderStatus.PAYMENT_PENDING, OrderStatus.PAID)]
    [InlineData(OrderStatus.PAID, OrderStatus.CONFIRMED)]
    [InlineData(OrderStatus.CONFIRMED, OrderStatus.COMPLETED)]
    [InlineData(OrderStatus.PAID, OrderStatus.REFUND_PENDING)]
    [InlineData(OrderStatus.CONFIRMED, OrderStatus.REFUND_PENDING)]
    [InlineData(OrderStatus.REFUND_PENDING, OrderStatus.REFUNDED)]
    public void ExpectedForwardTransitionsAreAllowed(OrderStatus from, OrderStatus to) =>
        Assert.True(OrderStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.CANCELLED, OrderStatus.PAID)]
    [InlineData(OrderStatus.COMPLETED, OrderStatus.CANCELLED)]
    [InlineData(OrderStatus.CREATED, OrderStatus.PAID)]
    [InlineData(OrderStatus.PAID, OrderStatus.PAYMENT_PENDING)]
    [InlineData(OrderStatus.REFUNDED, OrderStatus.PAID)]
    [InlineData(OrderStatus.FAILED, OrderStatus.PAYMENT_PENDING)]
    public void UnsafeTransitionsAreRejected(OrderStatus from, OrderStatus to) =>
        Assert.False(OrderStateMachine.CanTransition(from, to));

    [Fact]
    public void CancelledOrderCannotBeRevivedByPayment()
    {
        var order = new Order { Status = OrderStatus.CANCELLED };

        Assert.Throws<InvalidOperationException>(() =>
            OrderStateMachine.Transition(order, OrderStatus.PAID));
        Assert.Equal(OrderStatus.CANCELLED, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.COMPLETED)]
    [InlineData(OrderStatus.CANCELLED)]
    [InlineData(OrderStatus.FAILED)]
    [InlineData(OrderStatus.REFUNDED)]
    public void TerminalStatesCannotTransitionAnywhere(OrderStatus terminal)
    {
        foreach (var target in Enum.GetValues<OrderStatus>())
            Assert.False(OrderStateMachine.CanTransition(terminal, target));
    }
}
