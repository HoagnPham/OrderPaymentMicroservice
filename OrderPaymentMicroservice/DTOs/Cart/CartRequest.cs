namespace OrderPaymentMicroservice.DTOs.Cart;

public class CartItemResponse
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal PriceAtAdd { get; set; }
    public decimal SubTotal { get; set; }
}

public class CartResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal TotalPrice { get; set; }
    public List<CartItemResponse> Items { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

public class AddToCartRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class UpdateCartItemRequest
{
    public int Quantity { get; set; }
}
