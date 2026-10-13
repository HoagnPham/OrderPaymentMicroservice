using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderPaymentMicroservice.Data;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.DTOs.Payments;
using OrderPaymentMicroservice.DTOs.Products;
using OrderPaymentMicroservice.Security;
using OrderPaymentMicroservice.Services;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class MoneyPrecisionTests
{
    [Theory]
    [InlineData("25.001")] [InlineData("25.005")] [InlineData("0.019")] [InlineData("999999.999")]
    public async Task FractionalCentsAreRejectedAcrossAllRequestBoundaries(string input)
    {
        var money = decimal.Parse(input, CultureInfo.InvariantCulture);
        var request = new CreateOrderRequest { CustomerId = 1, TotalAmount = money, Items = [new() { ProductId = 1, Quantity = 1, UnitPrice = money }] };
        Assert.Throws<ArgumentException>(() => new InputValidator().ValidateCreateOrderRequest(request));
        request.TotalAmount = 25m;
        Assert.Throws<ArgumentException>(() => new InputValidator().ValidateCreateOrderRequest(request));
        Assert.Throws<ArgumentException>(() => new PaymentValidator().ValidateCreatePaymentRequest(new() { OrderId = 1, Amount = money, IdempotencyKey = "key" }));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ProductService(db, NullLogger<ProductService>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(new() { Name = "Fractional", Price = money, Stock = 1 }));
        Assert.Empty(db.Products);
    }
    [Theory]
    [InlineData("25.0100")] [InlineData("0.01")] [InlineData("999999.99")]
    public void CentAmountsIncludingTrailingZeroesAreAccepted(string input)
    {
        var money = decimal.Parse(input, CultureInfo.InvariantCulture);
        new InputValidator().ValidateCreateOrderRequest(new() { CustomerId = 1, TotalAmount = money, Items = [new() { ProductId = 1, Quantity = 1, UnitPrice = money }] });
        new PaymentValidator().ValidateCreatePaymentRequest(new() { OrderId = 1, Amount = money, IdempotencyKey = "key" });
    }
}
