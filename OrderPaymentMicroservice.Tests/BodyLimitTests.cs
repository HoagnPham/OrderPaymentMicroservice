using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OrderPaymentMicroservice.Security;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class BodyLimitTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task OverLimitBodiesAreRejectedWithOrWithoutContentLength(bool knownLength)
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/api/orders";
        context.Request.Body = new MemoryStream(new byte[1025]); if (knownLength) context.Request.ContentLength = 1025;
        var called = false;
        var middleware = new RequestBodyLimitMiddleware(_ => { called = true; return Task.CompletedTask; }, Options.Create(new ApiAdmissionOptions { MaxBodyBytes = 1024 }));
        await middleware.InvokeAsync(context);
        Assert.Equal(413, context.Response.StatusCode); Assert.False(called);
    }
    [Fact]
    public async Task ExactLimitBodyIsRewoundForJsonReader()
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/api/orders";
        var body = new byte[1024]; context.Request.Body = new MemoryStream(body);
        var middleware = new RequestBodyLimitMiddleware(async c => { Assert.Equal(0, c.Request.Body.Position); Assert.Equal(1024, c.Request.Body.Length); await c.Request.Body.CopyToAsync(Stream.Null); }, Options.Create(new ApiAdmissionOptions { MaxBodyBytes = 1024 }));
        await middleware.InvokeAsync(context); Assert.Equal(200, context.Response.StatusCode);
    }
    [Fact]
    public async Task ServerBodyLimitExceptionRemains413()
    {
        var context = new DefaultHttpContext(); context.Request.Path = "/api/orders"; context.Request.Body = new RejectedServerBody();
        var middleware = new RequestBodyLimitMiddleware(_ => throw new InvalidOperationException("Must not execute"), Options.Create(new ApiAdmissionOptions()));
        await middleware.InvokeAsync(context); Assert.Equal(413, context.Response.StatusCode);
    }
    private sealed class RejectedServerBody : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new BadHttpRequestException("Server body cap",413));
    }

}
