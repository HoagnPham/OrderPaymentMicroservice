using OrderPaymentMicroservice.Models;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class PendingAgeTests
{
    [Fact]
    public void UnknownReconciliationKeepsPendingAgeAndRefundStartsANewWindow()
    {
        var payment=new Payment();PaymentStateMachine.Transition(payment,PaymentStatus.PROCESSING);
        var since=payment.PendingSince;Assert.NotNull(since);
        PaymentStateMachine.Transition(payment,PaymentStatus.UNKNOWN);PaymentStateMachine.Transition(payment,PaymentStatus.PROCESSING);
        Assert.Equal(since,payment.PendingSince);
        PaymentStateMachine.Transition(payment,PaymentStatus.SUCCESS);Assert.Null(payment.PendingSince);
        PaymentStateMachine.Transition(payment,PaymentStatus.REFUND_PENDING);Assert.NotNull(payment.PendingSince);
        PaymentStateMachine.Transition(payment,PaymentStatus.REFUNDED);Assert.Null(payment.PendingSince);
    }
}
