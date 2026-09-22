using System.Security.Claims;

namespace OrderPaymentMicroservice.Security;

public static class ClaimsPrincipalExtensions
{
    public static int GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Authenticated user identifier is missing or invalid.");
    }

    public static bool CanAccessUser(this ClaimsPrincipal principal, int userId) =>
        principal.IsInRole("Admin") || principal.GetRequiredUserId() == userId;
}
