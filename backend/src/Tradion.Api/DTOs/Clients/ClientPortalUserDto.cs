namespace Tradion.Api.DTOs.Clients;

public class ClientPortalUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public string? RegistrationStatus { get; set; }
}
