using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrderPaymentMicroservice.Services.Payments;

/// <summary>
/// Local simulation of a provider that retains its ledger independently of the API process.
/// One atomic journal entry per operation/key binds retries to the original payload.
/// This is not a real payment provider or a distributed filesystem protocol.
/// </summary>
public sealed class DurableFakePaymentGateway : IPaymentGateway
{
    private readonly string _directory;
    public DurableFakePaymentGateway(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync("charge", request, true, cancellationToken);
    public Task<GatewayResult> RefundAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync("refund", request, true, cancellationToken);
    public Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync("charge", request, false, cancellationToken);
    public Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync("refund", request, false, cancellationToken);

    private async Task<GatewayResult> ExecuteAsync(string operation, GatewayPaymentRequest request, bool create, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 ||
            request.Amount <= 0 || request.PaymentId <= 0 || request.OrderId <= 0)
            throw new ArgumentException("Gateway request requires positive IDs/amount and a key of at most 128 characters");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation + ":" + request.IdempotencyKey)));
        var journal = Path.Combine(_directory, hash + ".json");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        // File sharing denies another API process access until the holder exits/disposes.
        await using var mutex = await AcquireAsync(journal + ".lock", budget.Token);
        if (File.Exists(journal))
        {
            var entry = JsonSerializer.Deserialize<LedgerEntry>(await File.ReadAllTextAsync(journal, budget.Token))
                ?? throw new InvalidDataException("Gateway journal is empty");
            if (entry.Operation != operation || entry.PaymentId != request.PaymentId || entry.OrderId != request.OrderId ||
                entry.Amount != request.Amount || entry.IdempotencyKey != request.IdempotencyKey ||
                (operation == "refund" && entry.ChargeReference != request.ExistingTransactionCode))
                throw new InvalidOperationException("Gateway key is already bound to a different payment payload");
            return entry.Result;
        }
        if (!create)
            return new GatewayResult(GatewayOutcome.Unknown, ErrorCode: "NOT_FOUND");

        var result = new GatewayResult(GatewayOutcome.Succeeded, $"DURABLE-FAKE-{operation}-{Guid.NewGuid():N}");
        var entryToWrite = new LedgerEntry(operation, request.PaymentId, request.OrderId, request.Amount, request.IdempotencyKey, operation == "refund" ? request.ExistingTransactionCode : null, result);
        var temporary = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entryToWrite, cancellationToken: budget.Token);
                stream.Flush(flushToDisk: true);
            }
            budget.Token.ThrowIfCancellationRequested();
            File.Move(temporary, journal); // Published only after the full record has been flushed.
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return result;
    }

    private static async Task<FileStream> AcquireAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(25, token); }
        }
    }

    private sealed record LedgerEntry(string Operation, int PaymentId, int OrderId, decimal Amount, string IdempotencyKey, string? ChargeReference, GatewayResult Result);
}
