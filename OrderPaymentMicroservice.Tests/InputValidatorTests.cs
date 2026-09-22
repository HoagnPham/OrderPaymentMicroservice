using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public class InputValidatorTests
{
    private readonly InputValidator validator = new();
    private static CreateOrderRequest Valid() => new()
    {
        CustomerId = 1, TotalAmount = 10m,
        Items = [new() { ProductId = 1, Quantity = 1, UnitPrice = 10m }]
    };

    [Fact]
    public void NullRequest() => Assert.Throws<ArgumentNullException>(() => validator.ValidateCreateOrderRequest(null!));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MinValue)]
    public void InvalidCustomer(int id)
    {
        var r = Valid(); r.CustomerId = id;
        Assert.Equal("CustomerId", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Theory]
    [InlineData("-1")] [InlineData("0")] [InlineData("0.009")]
    [InlineData("1000000")] [InlineData("79228162514264337593543950335")]
    public void InvalidTotal(string amount)
    {
        var r = Valid(); r.TotalAmount = Parse(amount);
        Assert.Equal("TotalAmount", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MissingItems(bool isNull)
    {
        var r = Valid(); r.Items = isNull ? null! : [];
        Assert.Equal("Items", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Fact]
    public void NullItem()
    {
        var r = Valid(); r.Items = [null!];
        Assert.Equal("item", Assert.Throws<ArgumentNullException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void InvalidProduct(int id)
    {
        var r = Valid(); r.Items[0].ProductId = id;
        Assert.Equal("ProductId", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(10001)] [InlineData(int.MaxValue)]
    public void InvalidQuantity(int quantity)
    {
        var r = Valid(); r.Items[0].Quantity = quantity;
        Assert.Equal("Quantity", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Theory]
    [InlineData("-0.01")] [InlineData("1000000")]
    [InlineData("79228162514264337593543950335")]
    public void InvalidPrice(string price)
    {
        var r = Valid(); r.Items[0].UnitPrice = Parse(price);
        Assert.Equal("UnitPrice", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r)).ParamName);
    }

    [Theory]
    [InlineData("0.01", 1, "0.01")]
    [InlineData("999999.99", 1, "999999.99")]
    [InlineData("100", 10000, "0.01")]
    [InlineData("10", 1, "10")]
    [InlineData("10.01", 1, "10")]
    [InlineData("9.99", 1, "10")]
    public void ValidBoundariesAndTolerance(string total, int quantity, string price)
    {
        var r = Valid(); r.TotalAmount = Parse(total);
        r.Items[0].Quantity = quantity; r.Items[0].UnitPrice = Parse(price);
        validator.ValidateCreateOrderRequest(r);
    }

    [Theory]
    [InlineData("10.011", "10")]
    [InlineData("9.989", "10")]
    [InlineData("0.01", "999999.99")]
    [InlineData("10", "0")]
    [InlineData("10", "0.001")]
    public void MismatchOrBelowMinimumPriceIsRejected(string total, string price)
    {
        var r = Valid(); r.TotalAmount = Parse(total); r.Items[0].UnitPrice = Parse(price);
        Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r));
    }

    [Fact]
    public void MultipleItems()
    {
        var r = Valid(); r.Items.Add(new() { ProductId = 2, Quantity = 2, UnitPrice = 5 });
        r.TotalAmount = 20; validator.ValidateCreateOrderRequest(r);
    }

    [Fact]
    public void ItemSubtotalAboveMaximumOrderTotalIsRejected()
    {
        var r = Valid(); r.Items[0].Quantity = 10000; r.Items[0].UnitPrice = 999999.99m;
        var exception = Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(r));
        Assert.Equal("TotalAmount", exception.ParamName);
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
