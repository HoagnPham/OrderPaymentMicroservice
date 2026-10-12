using System.ComponentModel.DataAnnotations;

namespace OrderPaymentMicroservice.Security;

internal static class RequestValidation
{
    public static void Validate(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            throw new ArgumentException(string.Join("; ", errors.Select(error => error.ErrorMessage)));
    }
}
