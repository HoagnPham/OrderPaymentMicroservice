using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;
using OrderPaymentMicroservice.Models;

namespace OrderPaymentMicroservice.Auditing;

public sealed class AuditTrailMiddleware(RequestDelegate next, ILogger<AuditTrailMiddleware> logger)
{
    private static readonly Dictionary<string, (string Action, string Resource)> Actions = new()
    {
        ["Orders.CreateOrder"] = ("ORDER_CREATE", "ORDER"), ["Orders.CancelOrder"] = ("ORDER_CANCEL", "ORDER"),
        ["Orders.ConfirmOrder"] = ("ORDER_CONFIRM", "ORDER"), ["Orders.CompleteOrder"] = ("ORDER_COMPLETE", "ORDER"),
        ["Payments.CreatePayment"] = ("PAYMENT_CREATE", "PAYMENT"), ["Payments.RetryPayment"] = ("PAYMENT_RETRY", "PAYMENT"),
        ["Payments.ReconcilePayment"] = ("PAYMENT_RECONCILE", "PAYMENT"), ["Payments.RefundPayment"] = ("PAYMENT_REFUND", "PAYMENT"),
        ["Payments.RetryRefund"] = ("PAYMENT_REFUND_RETRY", "PAYMENT"), ["Payments.ReconcileRefund"] = ("PAYMENT_REFUND_RECONCILE", "PAYMENT")
    };

    public async Task InvokeAsync(HttpContext context, IAuditWriter audit)
    {
        var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (endpoint == null || !Actions.TryGetValue(endpoint.ControllerName + "." + endpoint.ActionName, out var action))
        { await next(context); return; }
        var correlation = Guid.NewGuid();
        context.Items["AuditCorrelationId"] = correlation;
        var user = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? actor = int.TryParse(user, out var parsed) && parsed > 0 ? parsed : null;
        int? resource = int.TryParse(context.Request.RouteValues["id"]?.ToString(), out parsed) && parsed > 0 ? parsed : null;
        AuditEvent Entry(string phase, string outcome, int? status) => new()
        {
            CorrelationId = correlation, ActorKind = actor.HasValue ? "USER" : "ANONYMOUS", ActorUserId = actor,
            Action = action.Action, ResourceType = action.Resource, ResourceId = resource, Phase = phase, Outcome = outcome, HttpStatusCode = status
        };
        context.Response.Headers["X-Correlation-Id"] = correlation.ToString();
        try { await audit.AppendAsync(Entry("BEGIN", "STARTED", null)); }
        catch { await Unavailable(context, correlation, false); return; }

        // Persist result before copying an audited response to the transport.
        var original = context.Features.Get<IHttpResponseBodyFeature>() ?? new StreamResponseBodyFeature(context.Response.Body);
        var buffered = new BufferedAuditResponse();
        using var response = buffered.Buffer;
        context.Features.Set<IHttpResponseBodyFeature>(buffered);
        try
        {
            try { await next(context); }
            catch
            {
                await audit.AppendAsync(Entry("RESULT", "UNHANDLED_ERROR", 500));
                throw;
            }
            await buffered.Writer.FlushAsync(context.RequestAborted);
            if (!resource.HasValue && context.Response.Headers.Location.Count != 0)
            {
                var tail = context.Response.Headers.Location.ToString().Split('/').LastOrDefault();
                if (int.TryParse(tail, out parsed) && parsed > 0) resource = parsed;
            }
            var status = context.Response.StatusCode;
            var outcome = status < 400 ? "HTTP_SUCCESS" : status < 500 ? "HTTP_REJECTED" : "HTTP_FAILURE";
            try { await audit.AppendAsync(Entry("RESULT", outcome, status)); }
            catch
            {
                context.Features.Set<IHttpResponseBodyFeature>(original);
                await Unavailable(context, correlation, true); return;
            }
            response.Position = 0;
            context.Features.Set<IHttpResponseBodyFeature>(original);
            await response.CopyToAsync(original.Stream, context.RequestAborted);
        }
        finally { context.Features.Set<IHttpResponseBodyFeature>(original); await buffered.Writer.CompleteAsync(); }
    }

    private async Task Unavailable(HttpContext context, Guid correlation, bool mayHaveCommitted)
    {
        logger.LogError("Audit persistence failed for correlation {CorrelationId}; reconciliation may be required", correlation);
        if (context.Response.HasStarted) throw new InvalidOperationException("Audit persistence unavailable; reconciliation required");
        context.Response.Clear(); context.Response.StatusCode = 503;
        context.Response.Headers["X-Correlation-Id"] = correlation.ToString();
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            message = mayHaveCommitted ? "The operation may already have completed. Check order/payment status before retrying."
                : "The request was not executed. Please retry later.",
            requiresReconciliation = mayHaveCommitted, correlationId = correlation
        }));
    }
}
