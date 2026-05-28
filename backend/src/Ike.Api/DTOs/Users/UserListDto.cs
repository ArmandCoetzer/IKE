namespace Ike.Api.DTOs.Users;

public class UserListDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Occupation { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? SiteId { get; set; }
    public string? SiteName { get; set; }
    public string? CompanyName { get; set; }
    public string? RegistrationStatus { get; set; }
}
