using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using OrderPaymentMicroservice.Auditing;
using OrderPaymentMicroservice.Models;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class AuditMiddlewareTests
{
    [Fact]
    public async Task AuditCapturesOnlyAllowlistedFieldsAndServerCorrelation()
    {
        var context = Context(); context.Request.Headers.Authorization = "Bearer LEAK";
        context.Request.Headers["Idempotency-Key"] = "LEAK"; context.Request.Headers["X-Correlation-Id"] = "LEAK";
        context.Request.QueryString = new QueryString("?password=LEAK");
        var writer = new RecordingWriter();
        var middleware = new AuditTrailMiddleware(async c =>
        { Assert.Single(writer.Entries); c.Response.StatusCode = 201; c.Response.Headers.Location = "/api/orders/12"; await c.Response.WriteAsync("ok"); }, NullLogger<AuditTrailMiddleware>.Instance);
        await middleware.InvokeAsync(context, writer);
        Assert.Equal(2, writer.Entries.Count);
        Assert.Equal("BEGIN", writer.Entries[0].Phase); Assert.Equal("RESULT", writer.Entries[1].Phase);
        Assert.Equal("HTTP_SUCCESS", writer.Entries[1].Outcome); Assert.Equal(12, writer.Entries[1].ResourceId);
        Assert.Equal(1, writer.Entries[1].ActorUserId);
        Assert.Equal(writer.Entries[0].CorrelationId, writer.Entries[1].CorrelationId);
        Assert.Equal(writer.Entries[0].CorrelationId.ToString(), context.Response.Headers["X-Correlation-Id"].ToString());
        Assert.DoesNotContain("LEAK", JsonSerializer.Serialize(writer.Entries));
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, true)]
    public async Task AuditFailureRejectsResponseAndMarksPossibleCommittedOperation(int failAt, bool executed)
    {
        var context = Context(); var called = false; var writer = new RecordingWriter { FailAt = failAt };
        var middleware = new AuditTrailMiddleware(async c => { called = true; await c.Response.WriteAsync("original-success"); }, NullLogger<AuditTrailMiddleware>.Instance);
        await middleware.InvokeAsync(context, writer);
        Assert.Equal(executed, called); Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("original-success", text);
        using var payload = JsonDocument.Parse(text);
        Assert.Equal(executed, payload.RootElement.GetProperty("requiresReconciliation").GetBoolean());
        Assert.Equal(failAt - 1, writer.Entries.Count);
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test"));
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = "Orders", ActionName = "CreateOrder" }), "audit"));
        return context;
    }
    private sealed class RecordingWriter : IAuditWriter
    {
        private int _calls;
        public int FailAt { get; init; }
        public List<AuditEvent> Entries { get; } = [];
        public Task AppendAsync(AuditEvent entry)
        {
            if (++_calls == FailAt) throw new IOException("LEAK");
            Entries.Add(entry); return Task.CompletedTask;
        }
    }
}
