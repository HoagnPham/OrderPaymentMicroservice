using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrderPaymentMicroservice.Controllers;
using OrderPaymentMicroservice.DTOs.Orders;
using OrderPaymentMicroservice.Services.Interfaces;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class OrderRetryContractTests
{
    [Theory]
    [InlineData("40001",null,true,"ORDER_CONCURRENT_WRITE")]
    [InlineData("40P01",null,true,"ORDER_CONCURRENT_WRITE")]
    [InlineData("23505","IX_Orders_CustomerId_CreationKeyHash",true,"ORDER_CONCURRENT_WRITE")]
    [InlineData("23505","IX_Users_Email",false,"ORDER_PERSISTENCE_REJECTED")]
    [InlineData("23514","CK_Products_Stock_Nonnegative",false,"ORDER_PERSISTENCE_REJECTED")]
    [InlineData("P0001",null,false,"ORDER_PERSISTENCE_REJECTED")]
    public async Task OnlyAllowlistedDatabaseConflictsAreRetryable(string state,string? constraint,bool expected,string code)
    {
        var postgres=new PostgresException("PRIVATE_DATABASE_DIAGNOSTIC","ERROR","ERROR",state,constraintName:constraint);
        var controller=Controller(new DbUpdateException("PRIVATE_DATABASE_DIAGNOSTIC",postgres),"same-key");
        var result=Assert.IsType<ConflictObjectResult>(await controller.CreateOrder(new()));using var doc=JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal(code,doc.RootElement.GetProperty("code").GetString());Assert.Equal(expected,doc.RootElement.GetProperty("retryable").GetBoolean());Assert.DoesNotContain("PRIVATE_DATABASE_DIAGNOSTIC",doc.RootElement.ToString());
    }
    [Fact]
    public async Task WrappedSerializationConflictIsRecognizedButLegacyRequestCannotAutoRetry()
    {
        var wrapped=new InvalidOperationException("PRIVATE_WRAPPER",new PostgresException("PRIVATE_DATABASE_DIAGNOSTIC","ERROR","ERROR","40001"));
        foreach(var key in new string?[]{"same-key",null})
        {
            var result=Assert.IsType<ConflictObjectResult>(await Controller(wrapped,key).CreateOrder(new()));using var doc=JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
            Assert.Equal("ORDER_CONCURRENT_WRITE",doc.RootElement.GetProperty("code").GetString());Assert.Equal(key!=null,doc.RootElement.GetProperty("retryable").GetBoolean());Assert.DoesNotContain("PRIVATE_WRAPPER",doc.RootElement.ToString());
        }
    }
    private static OrdersController Controller(Exception error,string? key)
    {
        var http=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"1")],"test"))};if(key!=null)http.Request.Headers["Idempotency-Key"]=key;
        return new(new FailedOrderService(error),NullLogger<OrdersController>.Instance){ControllerContext=new(){HttpContext=http}};
    }
    private sealed class FailedOrderService(Exception error):IOrderService
    {
        public Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request,string? idempotencyKey=null)=>throw error;
        public Task<List<OrderResponse>> GetAllOrdersAsync()=>throw new NotSupportedException();
        public Task<List<OrderResponse>> GetOrdersByCustomerIdAsync(int id)=>throw new NotSupportedException();
        public Task<OrderResponse?> GetOrderByIdAsync(int id)=>throw new NotSupportedException();
        public Task<bool> CancelOrderAsync(int id)=>throw new NotSupportedException();
        public Task<OrderResponse> ConfirmOrderAsync(int id)=>throw new NotSupportedException();
        public Task<OrderResponse> CompleteOrderAsync(int id)=>throw new NotSupportedException();
    }
}
