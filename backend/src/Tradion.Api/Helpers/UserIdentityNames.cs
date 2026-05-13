using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;

namespace Tradion.Api.Helpers;

/// <summary>
/// Identity <see cref="ApplicationUser.UserName"/> must be globally unique. Email can repeat across companies;
/// we scope UserName by organization id so the same address can exist under different tenants.
/// </summary>
public static class UserIdentityNames
{
    public const int MaxUserNameLength = 256;

    public static string ScopedUserName(UserManager<ApplicationUser> userManager, Guid organizationId, string email)
    {
        var normalizedEmail = userManager.NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalizedEmail))
            normalizedEmail = email.Trim().ToUpperInvariant();
        var orgPart = organizationId.ToString("N");
        const string sep = "--";
        var maxSuffixLen = MaxUserNameLength - orgPart.Length - sep.Length;
        var suffix = normalizedEmail.Length > maxSuffixLen ? normalizedEmail[..maxSuffixLen] : normalizedEmail;
        return orgPart + sep + suffix;
    }

    public static async Task<ApplicationUser?> FindByEmailAndCompanyAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        Guid organizationId,
        string email,
        CancellationToken ct = default)
    {
        var ne = userManager.NormalizeEmail(email);
        if (string.IsNullOrEmpty(ne)) return null;
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.CompanyId == organizationId && u.NormalizedEmail == ne, ct);
    }
}
