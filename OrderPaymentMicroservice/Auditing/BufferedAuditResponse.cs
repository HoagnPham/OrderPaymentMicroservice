using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace OrderPaymentMicroservice.Auditing;

/// <summary>Defers transport Start/Complete until the audit RESULT is durable.</summary>
internal sealed class BufferedAuditResponse : IHttpResponseBodyFeature
{
    public MemoryStream Buffer { get; } = new();
    public Stream Stream => Buffer;
    public PipeWriter Writer { get; }
    public BufferedAuditResponse() => Writer = PipeWriter.Create(Buffer, new StreamPipeWriterOptions(leaveOpen: true));
    public void DisableBuffering() { }
    public Task StartAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public async Task CompleteAsync() { await Writer.FlushAsync(); }
    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Audited mutation endpoints cannot stream files");
}
