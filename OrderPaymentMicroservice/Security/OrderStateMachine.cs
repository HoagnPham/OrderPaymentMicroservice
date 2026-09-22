using OrderPaymentMicroservice.Models;

namespace OrderPaymentMicroservice.Security;

public static class OrderStateMachine
{
    public static bool CanTransition(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.CREATED, OrderStatus.VALIDATED) => true,
        (OrderStatus.VALIDATED, OrderStatus.PAYMENT_PENDING) => true,
        (OrderStatus.PAYMENT_PENDING, OrderStatus.PAID) => true,
        (OrderStatus.PAID, OrderStatus.CONFIRMED) => true,
        (OrderStatus.CONFIRMED, OrderStatus.COMPLETED) => true,
        (OrderStatus.CREATED or OrderStatus.VALIDATED or OrderStatus.PAYMENT_PENDING, OrderStatus.CANCELLED) => true,
        (OrderStatus.PAYMENT_PENDING, OrderStatus.FAILED) => true,
        _ => false
    };

    public static void Transition(Order order, OrderStatus target)
    {
        if (!CanTransition(order.Status, target))
        {
            throw new InvalidOperationException(
                $"Invalid order state transition: {order.Status} -> {target}");
        }

        order.Status = target;
    }
}
