using System.ComponentModel.DataAnnotations;

namespace Ike.Api.DTOs.Users;

public class UpdateUserRequest
{
    [StringLength(200)]
    public string? FullName { get; set; }

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

    [StringLength(50)]
    public string? Role { get; set; }

    public bool? IsActive { get; set; }

    public Guid? SiteId { get; set; }
    /// <summary> When true, clears the user's site assignment. </summary>
    public bool ClearSite { get; set; }
}
