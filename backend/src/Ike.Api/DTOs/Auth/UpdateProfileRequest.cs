using System.ComponentModel.DataAnnotations;

namespace Ike.Api.DTOs.Auth;

public class UpdateProfileRequest
{
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [StringLength(128)]
    public string? FirstName { get; set; }

    [StringLength(128)]
    public string? LastName { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }
}
