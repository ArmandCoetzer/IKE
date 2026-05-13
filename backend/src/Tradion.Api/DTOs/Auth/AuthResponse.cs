namespace Tradion.Api.DTOs.Auth;

public class AuthResponse
{
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public string? FullName { get; set; }
}
