namespace Tradion.Api.Helpers;

/// <summary>
/// Consistent JSON shape for validation and business-rule errors (400/404 bodies).
/// Web and mobile clients read <c>message</c> from the error payload.
/// </summary>
public static class ApiResponseBodies
{
    public static object Message(string message) => new { message };
}
