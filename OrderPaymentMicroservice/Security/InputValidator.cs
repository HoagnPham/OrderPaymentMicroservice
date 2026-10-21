using Microsoft.Extensions.Options;
using OrderPaymentMicroservice.DTOs.Orders;

namespace OrderPaymentMicroservice.Security;

/// <summary>
/// Order input validation: bounds, integer cents and total/item consistency.
/// SMT counterpart: Security/PaymentVerification.z3 (TEST2/TEST6/TEST7) and
/// Security/InventoryVerification.z3; fuzz counterpart: SecurityFuzzingTests.
/// </summary>
public class InputValidator
{
    private readonly int _maxItems;
    public InputValidator(IOptions<ApiAdmissionOptions>? options = null) => _maxItems = options?.Value.MaxOrderItems ?? 100;
    // Validation constants
    private const decimal MIN_ORDER_AMOUNT = 0.01m;
    private const decimal MAX_ORDER_AMOUNT = 999999.99m;
    private const int MIN_QUANTITY = 1;
    private const int MAX_QUANTITY = 10000;
    private const decimal MIN_PRICE = 0.01m;
    private const decimal MAX_PRICE = 999999.99m;

    /// <summary>
    /// Validate create order request. Boundary and overflow cases are covered by
    /// SecurityFuzzingTests and by SMT TEST2/TEST6/TEST7.
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

        MoneyAmounts.RequireCents(request.TotalAmount, nameof(request.TotalAmount));

        // Validate items
        if (request.Items == null || request.Items.Count == 0)
            throw new ArgumentException("Order must contain at least one item", nameof(request.Items));

        if (request.Items.Count > _maxItems)
            throw new ArgumentException($"Order may contain at most {_maxItems} items", nameof(request.Items));

        decimal calculatedTotal = 0;

        foreach (var item in request.Items)
        {
            ValidateOrderItem(item);
            calculatedTotal += item.Quantity * item.UnitPrice;
        }

        // TotalAmount must match the sum of items; modelled in PaymentVerification.z3 TEST2.
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
    /// Validate one order item: quantity, unit price and overflow.
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

        MoneyAmounts.RequireCents(item.UnitPrice, nameof(item.UnitPrice));

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
