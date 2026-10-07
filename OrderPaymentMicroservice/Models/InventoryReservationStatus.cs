namespace OrderPaymentMicroservice.Models;

/// <summary>
/// Tracks ownership of stock reserved for an order so compensation is
/// idempotent and stock can never be released twice.
/// </summary>
public enum InventoryReservationStatus
{
    RESERVED = 0,
    RELEASED = 1,
    CONSUMED = 2
}
