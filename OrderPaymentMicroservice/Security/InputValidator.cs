using OrderPaymentMicroservice.DTOs.Orders;

namespace OrderPaymentMicroservice.Security;

/// <summary>
/// Input validation for orders
/// This is the foundation for security analysis and fuzzing
/// TODO: Enhance with more sophisticated validation rules
/// TODO: Integrate with SMT solver for constraint verification
/// TODO: Add Fuzzing targets here
/// </summary>
public class InputValidator
{
    // Validation constants
    private const decimal MIN_ORDER_AMOUNT = 0.01m;
    private const decimal MAX_ORDER_AMOUNT = 999999.99m;
    private const int MIN_QUANTITY = 1;
    private const int MAX_QUANTITY = 10000;
    private const decimal MIN_PRICE = 0.01m;
    private const decimal MAX_PRICE = 999999.99m;

    /// <summary>
    /// Validate create order request
    /// TODO: Fuzzing target - test boundary conditions and overflow
    /// TODO: SMT verification - amount consistency
    /// </summary>
    public void ValidateCreateOrderRequest(CreateOrderRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request), "Order request cannot be null");

        // Validate customer ID
        if (request.CustomerId <= 0)
            throw new ArgumentException("CustomerId must be greater than 0", nameof(request.CustomerId));

        // Validate total amount
        if (request.TotalAmount <= 0)
            throw new ArgumentException("TotalAmount must be greater than 0", nameof(request.TotalAmount));

        if (request.TotalAmount < MIN_ORDER_AMOUNT)
            throw new ArgumentException($"TotalAmount must be at least {MIN_ORDER_AMOUNT}", nameof(request.TotalAmount));

        if (request.TotalAmount > MAX_ORDER_AMOUNT)
            throw new ArgumentException($"TotalAmount cannot exceed {MAX_ORDER_AMOUNT}", nameof(request.TotalAmount));

        // Validate items
        if (request.Items == null || request.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item", nameof(request.Items));

        decimal calculatedTotal = 0;

        foreach (var item in request.Items)
        {
            ValidateOrderItem(item);
            calculatedTotal += item.Quantity * item.UnitPrice;
        }

        // TODO: SMT verification - TotalAmount must match sum of items
        // Monetary values are stored with two decimal places, so a difference of
        // one cent is the largest rounding difference accepted at this boundary.
        const decimal tolerance = 0.01m;
        if (Math.Abs(request.TotalAmount - calculatedTotal) > tolerance)
        {
            throw new ArgumentException(
                "TotalAmount must match the sum of order items",
                nameof(request.TotalAmount));
        }
    }

    /// <summary>
    /// Validate order item
    /// TODO: Fuzzing target - test invalid quantities and prices
    /// </summary>
    private void ValidateOrderItem(CreateOrderItemRequest item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item), "Order item cannot be null");

        // Product ID validation
        if (item.ProductId <= 0)
            throw new ArgumentException("ProductId must be greater than 0", nameof(item.ProductId));

        // Quantity validation
        if (item.Quantity < MIN_QUANTITY)
            throw new ArgumentException($"Quantity must be at least {MIN_QUANTITY}", nameof(item.Quantity));

        if (item.Quantity > MAX_QUANTITY)
            throw new ArgumentException($"Quantity cannot exceed {MAX_QUANTITY}", nameof(item.Quantity));

        // Unit price validation
        if (item.UnitPrice < MIN_PRICE)
            throw new ArgumentException($"UnitPrice must be at least {MIN_PRICE}", nameof(item.UnitPrice));

        if (item.UnitPrice > MAX_PRICE)
            throw new ArgumentException($"UnitPrice cannot exceed {MAX_PRICE}", nameof(item.UnitPrice));

        // Check for overflow in subtotal calculation
        try
        {
            var subtotal = checked(item.Quantity * item.UnitPrice);
            if (subtotal < 0)
                throw new OverflowException("SubTotal calculation resulted in negative value");
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("Item calculation would overflow", nameof(item), ex);
        }
    }
}
