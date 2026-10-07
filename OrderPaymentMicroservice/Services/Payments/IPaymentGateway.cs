namespace OrderPaymentMicroservice.Services.Payments;

public enum GatewayOutcome
{
    Succeeded,
    Declined,
    Unknown
}

public sealed record GatewayPaymentRequest(
    int PaymentId,
    int OrderId,
    decimal Amount,
    string IdempotencyKey,
    string? ExistingTransactionCode = null);

public sealed record GatewayResult(
    GatewayOutcome Outcome,
    string? TransactionCode = null,
    string? ErrorCode = null,
    string? Message = null);

public interface IPaymentGateway
{
    Task<GatewayResult> ChargeAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default);

    Task<GatewayResult> RefundAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default);

    Task<GatewayResult> GetChargeStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default);

    Task<GatewayResult> GetRefundStatusAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default);
}
