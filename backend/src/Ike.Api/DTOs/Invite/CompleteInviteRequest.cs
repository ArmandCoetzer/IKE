using System.ComponentModel.DataAnnotations;

namespace Ike.Api.DTOs.Invite;

public class CompleteInviteRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [StringLength(128)]
    public string? FirstName { get; set; }

    [StringLength(128)]
    public string? LastName { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Compare(nameof(Password))]
    [DataType(DataType.Password)]
    public string? ConfirmPassword { get; set; }
}
