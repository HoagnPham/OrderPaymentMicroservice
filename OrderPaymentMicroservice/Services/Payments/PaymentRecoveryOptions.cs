namespace OrderPaymentMicroservice.Services.Payments;

public sealed class PaymentRecoveryOptions
{
    public const string SectionName = "PaymentRecovery";

    public bool Enabled { get; set; } = true;
    public int ScanIntervalSeconds { get; set; } = 30;
    public int StaleAfterSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 50;
}
