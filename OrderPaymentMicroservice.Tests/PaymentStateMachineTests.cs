using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class PaymentStateMachineTests
{
    [Theory]
    [InlineData(PaymentStatus.PENDING, PaymentStatus.PROCESSING)]
    [InlineData(PaymentStatus.PROCESSING, PaymentStatus.SUCCESS)]
    [InlineData(PaymentStatus.PROCESSING, PaymentStatus.FAILED)]
    [InlineData(PaymentStatus.PROCESSING, PaymentStatus.UNKNOWN)]
    [InlineData(PaymentStatus.UNKNOWN, PaymentStatus.PROCESSING)]
    [InlineData(PaymentStatus.UNKNOWN, PaymentStatus.FAILED)]
    [InlineData(PaymentStatus.SUCCESS, PaymentStatus.REFUND_PENDING)]
    [InlineData(PaymentStatus.REFUND_PENDING, PaymentStatus.REFUNDED)]
    public void DeclaredTransitionsAreAllowed(PaymentStatus from, PaymentStatus to) =>
        Assert.True(PaymentStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(PaymentStatus.FAILED)]
    [InlineData(PaymentStatus.REFUNDED)]
    public void FailureAndRefundAreTerminal(PaymentStatus terminal)
    {
        foreach (var target in Enum.GetValues<PaymentStatus>())
            Assert.False(PaymentStateMachine.CanTransition(terminal, target));
    }

    [Fact]
    public void SuccessfulPaymentCanOnlyEnterRefundFlow() =>
        Assert.Equal(
            [PaymentStatus.REFUND_PENDING],
            Enum.GetValues<PaymentStatus>().Where(target =>
                PaymentStateMachine.CanTransition(PaymentStatus.SUCCESS, target)));
}
