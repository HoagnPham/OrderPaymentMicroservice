using OrderPaymentMicroservice.Models;

namespace OrderPaymentMicroservice.Security;

public static class PaymentStateMachine
{
    public static bool IsTerminal(PaymentStatus status) => status is
        PaymentStatus.FAILED or PaymentStatus.REFUNDED;

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) => (from, to) switch
    {
        (PaymentStatus.PENDING, PaymentStatus.PROCESSING) => true,
        (PaymentStatus.PROCESSING, PaymentStatus.SUCCESS or PaymentStatus.FAILED or PaymentStatus.UNKNOWN) => true,
        (PaymentStatus.UNKNOWN, PaymentStatus.PROCESSING or PaymentStatus.FAILED) => true,
        (PaymentStatus.SUCCESS, PaymentStatus.REFUND_PENDING) => true,
        (PaymentStatus.REFUND_PENDING, PaymentStatus.REFUNDED) => true,
        _ => false
    };

    public static void Transition(Payment payment, PaymentStatus target)
    {
        if (!CanTransition(payment.Status, target))
            throw new InvalidOperationException($"Invalid payment state transition: {payment.Status} -> {target}");

        var waiting = target is PaymentStatus.PROCESSING or PaymentStatus.UNKNOWN or PaymentStatus.REFUND_PENDING;
        payment.PendingSince = waiting ? payment.PendingSince ?? DateTime.UtcNow : null;
        payment.Status = target;
        payment.UpdatedAt = DateTime.UtcNow;
        payment.Version++;
    }
}
