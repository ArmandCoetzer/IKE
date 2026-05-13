namespace Tradion.Api.DTOs.Invite;

public class InviteInfoDto
{
    public string Type { get; set; } = string.Empty; // "client" | "employee"
    public string Email { get; set; } = string.Empty;
}
