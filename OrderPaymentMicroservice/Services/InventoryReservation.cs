using Microsoft.EntityFrameworkCore;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.Models;

namespace OrderPaymentMicroservice.Services;

internal static class InventoryReservation
{
    public static async Task ReleaseAsync(AppDbContext context, Order order)
    {
        if (order.InventoryStatus == InventoryReservationStatus.RELEASED)
            return;
        if (order.InventoryStatus == InventoryReservationStatus.CONSUMED)
            throw new InvalidOperationException("Consumed inventory cannot be released");

        var productIds = order.OrderItems.Select(item => item.ProductId).Distinct().ToList();
        var products = await context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("Cannot release inventory because one or more products no longer exist");

        foreach (var item in order.OrderItems)
        {
            var product = products[item.ProductId];
            product.Stock = checked(product.Stock + item.Quantity);
            product.UpdatedAt = DateTime.UtcNow;
        }

        order.InventoryStatus = InventoryReservationStatus.RELEASED;
        order.UpdatedAt = DateTime.UtcNow;
    }
}
