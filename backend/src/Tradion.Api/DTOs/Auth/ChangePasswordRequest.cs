using System.ComponentModel.DataAnnotations;

namespace Tradion.Api.DTOs.Auth;

public class ChangePasswordRequest
{
    [Required]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;
}
