namespace Tradion.Api.DTOs.Clients;

public class ClientDto
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    /// <summary>True when a new client portal user was created; false when email was already registered (company still created).</summary>
    public bool? PortalUserCreated { get; set; }
    /// <summary>Shown after create when no portal user was created (e.g. email already in use elsewhere).</summary>
    public string? PortalMessage { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateClientRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? UserId { get; set; }
}

public class UpdateClientRequest
{
    public string? CompanyName { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? UserId { get; set; }
    public bool? IsActive { get; set; }
}
