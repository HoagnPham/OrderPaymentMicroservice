using Microsoft.Extensions.Options;

namespace OrderPaymentMicroservice.Security;

/// <summary>Enforces the same byte limit for known Content-Length and streaming bodies before JSON parsing.</summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IOptions<ApiAdmissionOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) { await next(context); return; }
        var limit = options.Value.MaxBodyBytes;
        if (context.Request.ContentLength > limit) { await TooLarge(context); return; }
        var original = context.Request.Body;
        using var buffered = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await original.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)buffered.Length)), context.RequestAborted);
                if (read == 0) break;
                await buffered.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                if (buffered.Length > limit) { await TooLarge(context); return; }
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        { await TooLarge(context); return; }
        buffered.Position = 0; context.Request.Body = buffered;
        try { await next(context); } finally { context.Request.Body = original; }
    }
    private static async Task TooLarge(HttpContext context)
    {
        context.Response.StatusCode = 413; context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"message\":\"The request is too large.\"}");
    }

}
