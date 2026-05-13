using System.ComponentModel.DataAnnotations;

namespace Tradion.Api.DTOs.Users;

public class CreateUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(128)]
    public string? FirstName { get; set; }

    [StringLength(128)]
    public string? LastName { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Occupation { get; set; }

    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required]
    [StringLength(50)]
    public string Role { get; set; } = string.Empty;

    public Guid? SiteId { get; set; }
}
