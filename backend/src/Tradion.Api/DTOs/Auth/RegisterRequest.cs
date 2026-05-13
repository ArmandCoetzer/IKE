using System.ComponentModel.DataAnnotations;

namespace Tradion.Api.DTOs.Auth;

public class RegisterRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [StringLength(200)]
    public string? FullName { get; set; }

    [Required]
    [StringLength(256)]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? CompanyAddress { get; set; }

    [StringLength(50)]
    public string? CompanyPhone { get; set; }
}
