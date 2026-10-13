using Microsoft.Extensions.Options;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class AdmissionBoundaryTests
{
    [Theory]
    [InlineData(3, true)] [InlineData(4, false)]
    public void ItemCountLimitRejectsBeforeProcessingItems(int count, bool accepted)
    {
        var validator = new InputValidator(Options.Create(new ApiAdmissionOptions { MaxOrderItems = 3 }));
        var request = new CreateOrderRequest { CustomerId = 1, TotalAmount = count,
            Items = Enumerable.Range(0,count).Select(_ => new CreateOrderItemRequest { ProductId = 1, Quantity = 1, UnitPrice = 1m }).ToList() };
        if (accepted) validator.ValidateCreateOrderRequest(request);
        else Assert.Equal("Items", Assert.Throws<ArgumentException>(() => validator.ValidateCreateOrderRequest(request)).ParamName);
    }
}
