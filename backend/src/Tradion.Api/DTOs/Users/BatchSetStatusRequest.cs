using System.ComponentModel.DataAnnotations;

namespace Tradion.Api.DTOs.Users;

public class BatchSetStatusRequest
{
    [Required]
    [MinLength(1)]
    public List<string> UserIds { get; set; } = new();

    [Required]
    public bool IsActive { get; set; }
}
