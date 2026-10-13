namespace OrderPaymentMicroservice.Services;

/// <summary>A known business rejection; never an instruction to retry automatically.</summary>
public sealed class OrderCreationConflictException(string code, string message):InvalidOperationException(message)
{
    public string Code { get; } = code;
}
