using System.ComponentModel.DataAnnotations;

namespace OrderPaymentMicroservice.Security;

public static class MoneyAmounts
{
    // Decimal.Round avoids multiplication overflow for decimal.MaxValue.
    // Trailing zeroes (25.010m) represent a cent value and remain valid.
    public static void RequireCents(decimal value, string parameter)
    {
        if (decimal.Round(value, 2) != value)
            throw new ArgumentException("Money must be a multiple of 0.01; values are not silently rounded", parameter);
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MoneyAmountAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || value is decimal amount && decimal.Round(amount, 2) == amount;
    public override string FormatErrorMessage(string name) => $"{name} must be a multiple of 0.01; values are not silently rounded";
}
