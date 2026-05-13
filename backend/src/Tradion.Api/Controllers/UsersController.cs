using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Users;
using Tradion.Api.Helpers;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserService _currentUser;

    public UsersController(UserManager<ApplicationUser> userManager, ApplicationDbContext db, IEmailService emailService, IConfiguration configuration, ICurrentUserService currentUser)
    {
        _userManager = userManager;
        _db = db;
        _emailService = emailService;
        _configuration = configuration;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewUsers")]
    [ProducesResponseType(typeof(List<UserListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserListDto>>> List([FromQuery] string? role, [FromQuery] bool? isActive, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<ApplicationUser> query = _db.Users.AsNoTracking().Include(u => u.Company).Include(u => u.Site);
        if (companyId.HasValue)
        {
            var ourClientIds = await _db.Companies.AsNoTracking()
                .Where(c => c.ParentCompanyId == companyId)
                .Select(c => c.Id)
                .ToListAsync(ct);
            query = query.Where(u => u.CompanyId == companyId || (u.CompanyId.HasValue && ourClientIds.Contains(u.CompanyId.Value)));
        }
        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var users = await query.ToListAsync(ct);
        var result = new List<UserListDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var userRole = roles.FirstOrDefault();
            if (role != null && userRole != role)
                continue;

            result.Add(MapToUserListDto(user, userRole));
        }

        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "RequireViewUsers")]
    [ProducesResponseType(typeof(UserListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserListDto>> Get(string id, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        var user = await _db.Users
            .Include(u => u.Company).Include(u => u.Site)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
            return NotFound();
        if (companyId.HasValue && user.CompanyId != companyId)
        {
            var isOurClient = await _db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.ParentCompanyId == companyId, ct);
            if (!isOurClient)
                return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(MapToUserListDto(user, roles.FirstOrDefault()));
    }

    private static UserListDto MapToUserListDto(ApplicationUser user, string? role) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        FullName = user.FullName ?? string.Empty,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Phone = user.PhoneNumber,
        Occupation = user.Occupation,
        Role = role,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        SiteId = user.SiteId,
        SiteName = user.Site?.Name,
        CompanyName = user.Company?.Name,
        RegistrationStatus = user.RegistrationStatus
    };

    [HttpGet("roles")]
    [Authorize(Policy = "RequireViewUsers")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public ActionResult<List<string>> GetRoles([FromQuery] bool excludeClient = false)
    {
        var roles = new List<string> { SeedData.RoleAdmin, SeedData.RoleManager, SeedData.RoleCoordinator, SeedData.RoleTechnician, SeedData.RoleClient };
        if (excludeClient)
            roles.Remove(SeedData.RoleClient);
        return Ok(roles);
    }

    [HttpPost]
    [Authorize(Policy = "RequireEditUsers")]
    [ProducesResponseType(typeof(UserListDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserListDto>> Create([FromBody] CreateUserRequest request)
    {
        var employeeRoles = new[] { SeedData.RoleAdmin, SeedData.RoleManager, SeedData.RoleCoordinator, SeedData.RoleTechnician };
        if (!employeeRoles.Contains(request.Role))
            return BadRequest(new { message = "Invalid role. Client users are created when adding a client." });

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        if (currentUser?.CompanyId == null)
            return BadRequest(new { message = "Your account is not linked to a company. Only users in a company can invite employees." });

        var dupInCompany = await UserIdentityNames.FindByEmailAndCompanyAsync(
            _userManager, _db, currentUser.CompanyId.Value, request.Email);
        if (dupInCompany != null)
            return BadRequest(new { message = "A user with this email already exists in your organization." });

        if (request.SiteId.HasValue)
        {
            var allowedSiteIds = await GetAllowedSiteIdsAsync(currentUser.CompanyId.Value, default);
            if (!allowedSiteIds.Contains(request.SiteId.Value))
                return BadRequest(new { message = "The selected site is not in your scope." });
        }

        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";
        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrEmpty(s))).Trim();
        if (string.IsNullOrEmpty(fullName)) fullName = request.Email;

        var user = new ApplicationUser
        {
            UserName = UserIdentityNames.ScopedUserName(_userManager, currentUser.CompanyId.Value, request.Email),
            Email = request.Email,
            EmailConfirmed = true,
            FullName = fullName,
            FirstName = string.IsNullOrEmpty(firstName) ? null : firstName,
            LastName = string.IsNullOrEmpty(lastName) ? null : lastName,
            PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Occupation = string.IsNullOrWhiteSpace(request.Occupation) ? null : request.Occupation.Trim(),
            CompanyId = currentUser.CompanyId,
            SiteId = request.SiteId
        };

        string password;
        if (!string.IsNullOrEmpty(request.Password))
        {
            password = request.Password;
            user.RegistrationStatus = SeedData.RegistrationStatusRegistered;
        }
        else
        {
            password = "Temp1!" + Guid.NewGuid().ToString("N")[..20];
            user.RegistrationStatus = SeedData.RegistrationStatusInvited;
            user.InviteToken = Guid.NewGuid().ToString("N");
            user.InviteTokenExpiry = DateTime.UtcNow.AddDays(7);
        }

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        await _userManager.AddToRoleAsync(user, request.Role);

        var created = await _db.Users.Include(u => u.Company).Include(u => u.Site).FirstAsync(u => u.Id == user.Id, default);

        if (user.RegistrationStatus == SeedData.RegistrationStatusInvited)
        {
            var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:4201";
            var inviteLink = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(user.InviteToken!)}";
            await _emailService.SendEmployeeInviteEmailAsync(request.Email, inviteLink, fullName);
        }

        return CreatedAtAction(nameof(Get), new { id = user.Id }, MapToUserListDto(created, request.Role));
    }

    [HttpPost("{id}/re-invite")]
    [Authorize(Policy = "RequireEditUsers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ReInvite(string id, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound(new { message = "User not found." });

        if (companyId.HasValue && user.CompanyId != companyId)
        {
            var isOurClient = await _db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.ParentCompanyId == companyId, ct);
            if (!isOurClient)
                return NotFound(new { message = "User not found." });
        }

        if (!string.Equals(user.RegistrationStatus, SeedData.RegistrationStatusInvited, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "User is not in invited status." });
        if (!user.IsActive)
            return BadRequest(new { message = "User is inactive." });
        if (string.IsNullOrWhiteSpace(user.Email))
            return BadRequest(new { message = "User has no email address." });

        user.InviteToken = Guid.NewGuid().ToString("N");
        user.InviteTokenExpiry = DateTime.UtcNow.AddDays(7);
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return BadRequest(new { message = string.Join(" ", update.Errors.Select(e => e.Description)) });

        var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:4201";
        var inviteLink = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(user.InviteToken)}";
        await _emailService.SendEmployeeInviteEmailAsync(user.Email, inviteLink, user.FullName ?? user.Email);

        return Ok(new { message = "Invite sent." });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "RequireEditUsers")]
    [ProducesResponseType(typeof(UserListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserListDto>> Update(string id, [FromBody] UpdateUserRequest request, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();
        if (companyId.HasValue && user.CompanyId != companyId)
        {
            var isOurClient = await _db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.ParentCompanyId == companyId, ct);
            if (!isOurClient)
                return NotFound();
        }

        if (request.FullName != null)
            user.FullName = request.FullName.Trim();
        if (request.FirstName != null)
            user.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim();
        if (request.LastName != null)
            user.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();
        if (request.FirstName != null || request.LastName != null)
        {
            var fn = user.FirstName ?? "";
            var ln = user.LastName ?? "";
            user.FullName = string.Join(" ", new[] { fn, ln }.Where(s => !string.IsNullOrEmpty(s))).Trim();
            if (string.IsNullOrEmpty(user.FullName)) user.FullName = user.Email ?? "";
        }
        if (request.Phone != null)
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (request.Occupation != null)
            user.Occupation = string.IsNullOrWhiteSpace(request.Occupation) ? null : request.Occupation.Trim();
        var currentRoles = await _userManager.GetRolesAsync(user);
        var isAdminUser = currentRoles.Contains(SeedData.RoleAdmin);
        if (request.IsActive.HasValue)
        {
            if (isAdminUser && !request.IsActive.Value)
                return BadRequest(new { message = "Admin users cannot be deactivated." });
            user.IsActive = request.IsActive.Value;
        }
        if (!string.IsNullOrEmpty(request.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await _userManager.ResetPasswordAsync(user, token, request.Password);
            if (!resetResult.Succeeded)
                return BadRequest(new { message = string.Join(" ", resetResult.Errors.Select(e => e.Description)) });
        }
        if (!string.IsNullOrEmpty(request.Role))
        {
            var validRoles = new[] { SeedData.RoleAdmin, SeedData.RoleManager, SeedData.RoleCoordinator, SeedData.RoleTechnician, SeedData.RoleClient };
            if (!validRoles.Contains(request.Role))
                return BadRequest(new { message = "Invalid role." });
            if (isAdminUser && !string.Equals(request.Role, SeedData.RoleAdmin, StringComparison.Ordinal))
                return BadRequest(new { message = "Admin users cannot be removed from the Admin role." });
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, request.Role);
        }

        if (request.ClearSite)
            user.SiteId = null;
        else if (request.SiteId.HasValue)
        {
            if (!companyId.HasValue)
                return BadRequest(new { message = "Your account is not linked to a company." });
            var allowedSiteIds = await GetAllowedSiteIdsAsync(companyId.Value, ct);
            if (!allowedSiteIds.Contains(request.SiteId.Value))
                return BadRequest(new { message = "The selected site is not in your scope." });
            user.SiteId = request.SiteId;
        }

        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var updated = await _db.Users.Include(u => u.Company).Include(u => u.Site).FirstAsync(u => u.Id == user.Id, ct);
        return Ok(MapToUserListDto(updated, roles.FirstOrDefault()));
    }

    [HttpPost("batch-set-status")]
    [Authorize(Policy = "RequireEditUsers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> BatchSetStatus([FromBody] BatchSetStatusRequest request, CancellationToken ct = default)
    {
        if (request.UserIds == null || request.UserIds.Count == 0)
            return BadRequest(new { message = "At least one user ID is required." });

        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        List<Guid>? ourClientIds = null;
        if (companyId.HasValue)
        {
            ourClientIds = await _db.Companies.AsNoTracking()
                .Where(c => c.ParentCompanyId == companyId)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        var users = await _userManager.Users.Where(u => request.UserIds.Contains(u.Id)).ToListAsync(ct);
        if (!request.IsActive)
        {
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains(SeedData.RoleAdmin))
                    return BadRequest(new { message = "Admin users cannot be deactivated." });
            }
        }
        foreach (var user in users)
        {
            if (companyId.HasValue)
            {
                var inScope = user.CompanyId == companyId || (user.CompanyId.HasValue && ourClientIds!.Contains(user.CompanyId.Value));
                if (!inScope)
                    continue;
            }
            user.IsActive = request.IsActive;
            await _userManager.UpdateAsync(user);
        }

        return Ok();
    }

    private async Task<List<Guid>> GetAllowedSiteIdsAsync(Guid companyId, CancellationToken ct)
    {
        var ourClientIds = await _db.Companies.AsNoTracking()
            .Where(c => c.ParentCompanyId == companyId)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var allowedCompanyIds = new List<Guid> { companyId };
        allowedCompanyIds.AddRange(ourClientIds);

        return await _db.Sites.AsNoTracking()
            .Where(s => s.CompanyId.HasValue && allowedCompanyIds.Contains(s.CompanyId.Value))
            .Select(s => s.Id)
            .ToListAsync(ct);
    }
}
