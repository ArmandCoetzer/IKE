using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Text;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Auth;
using Tradion.Api.Helpers;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPermissionService _permissionService;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuthController> _logger;
    private readonly IEmailService _emailService;
    private readonly IHostEnvironment _environment;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenService jwtTokenService,
        IPermissionService permissionService,
        IConfiguration configuration,
        ApplicationDbContext db,
        ILogger<AuthController> logger,
        IEmailService emailService,
        IHostEnvironment environment)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
        _permissionService = permissionService;
        _configuration = configuration;
        _db = db;
        _logger = logger;
        _emailService = emailService;
        _environment = environment;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        // Self-register is disabled outside integration tests; default admin + invites are used in real deployments.
        if (!_environment.IsEnvironment("Testing"))
        {
            await Task.CompletedTask;
            return BadRequest(new { message = "Self-registration is disabled. Please contact your administrator." });
        }

        try
        {
            if (string.IsNullOrWhiteSpace(request.CompanyName))
                return BadRequest(new { message = "Company name is required." });

            var company = new Company
            {
                Id = Guid.NewGuid(),
                Name = request.CompanyName.Trim(),
                Type = CompanyType.Main,
                ParentCompanyId = null,
                ContactEmail = request.Email,
                ContactPhone = string.IsNullOrWhiteSpace(request.CompanyPhone) ? null : request.CompanyPhone.Trim(),
                Address = string.IsNullOrWhiteSpace(request.CompanyAddress) ? null : request.CompanyAddress.Trim(),
                IsActive = true
            };
            _db.Companies.Add(company);

            var user = new ApplicationUser
            {
                UserName = UserIdentityNames.ScopedUserName(_userManager, company.Id, request.Email),
                Email = request.Email,
                EmailConfirmed = true,
                FullName = request.FullName ?? request.Email,
                CompanyId = company.Id
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

            await _db.SaveChangesAsync();

            await _userManager.AddToRoleAsync(user, SeedData.RoleAdmin);

            var token = _jwtTokenService.GenerateToken(user, SeedData.RoleAdmin);
            var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");
            var permissions = await _permissionService.GetEffectivePermissionNamesAsync(user);

            return Ok(new AuthResponse
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
                Email = user.Email ?? string.Empty,
                Role = SeedData.RoleAdmin,
                Permissions = permissions,
                FullName = user.FullName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", request?.Email);
            return BadRequest(new { message = ex.Message, detail = ex.ToString() });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct = default)
    {
        // Backward-compatible endpoint. Prefer /login-web and /login-mobile.
        return await LoginMobile(request, ct);
    }

    [HttpPost("login-web")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> LoginWeb([FromBody] LoginRequest request, CancellationToken ct = default)
    {
        var result = await AuthenticateCredentialsAsync(request, ct);
        if (result.User == null || result.Role == null)
            return Unauthorized(new { message = result.ErrorMessage ?? "Invalid email or password." });
        if (string.Equals(result.Role, SeedData.RoleTechnician, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { message = "Technician accounts can only sign in on the mobile app." });
        return Ok(result.Response);
    }

    [HttpPost("login-mobile")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> LoginMobile([FromBody] LoginRequest request, CancellationToken ct = default)
    {
        var result = await AuthenticateCredentialsAsync(request, ct);
        if (result.User == null)
            return Unauthorized(new { message = result.ErrorMessage ?? "Invalid email or password." });
        return Ok(result.Response);
    }

    private async Task<(ApplicationUser? User, string? Role, AuthResponse? Response, string? ErrorMessage)> AuthenticateCredentialsAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = _userManager.NormalizeEmail(request.Email);
        if (string.IsNullOrEmpty(normalized))
            return (null, null, null, "Invalid email or password.");

        var candidates = await _db.Users
            .Where(u => u.NormalizedEmail == normalized && u.IsActive)
            .OrderBy(u => u.Id)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return (null, null, null, "Invalid email or password.");

        ApplicationUser? user = null;
        foreach (var candidate in candidates)
        {
            var result = await _signInManager.CheckPasswordSignInAsync(candidate, request.Password, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                user = candidate;
                break;
            }
        }

        if (user == null)
            return (null, null, null, "Invalid email or password.");

        if (user.RegistrationStatus == SeedData.RegistrationStatusInvited)
            return (null, null, null, "Please complete your registration using the link sent to your email.");

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault();
        var token = _jwtTokenService.GenerateToken(user, role);
        var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");
        var permissions = await _permissionService.GetEffectivePermissionNamesAsync(user);

        return (user, role, new AuthResponse
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Email = user.Email ?? string.Empty,
            Role = role,
            Permissions = permissions,
            FullName = user.FullName
        }, null);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault();
        var token = _jwtTokenService.GenerateToken(user, role);
        var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");
        var permissions = await _permissionService.GetEffectivePermissionNamesAsync(user);

        return Ok(new AuthResponse
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Email = user.Email ?? string.Empty,
            Role = role,
            Permissions = permissions,
            FullName = user.FullName
        });
    }

    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileDto>> GetProfile()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
            return Unauthorized();

        return Ok(new ProfileDto
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber
        });
    }

    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
            return Unauthorized();

        var beforeEmail = user.Email ?? string.Empty;
        var beforeFirstName = user.FirstName;
        var beforeLastName = user.LastName;
        var beforePhone = user.PhoneNumber;
        var beforeFullName = user.FullName;
        var changes = new List<string>();

        if (request.Email != null)
        {
            var newEmail = request.Email.Trim();
            if (string.IsNullOrWhiteSpace(newEmail))
                return BadRequest(new { message = "Email cannot be empty." });

            var normalized = _userManager.NormalizeEmail(newEmail);
            if (string.IsNullOrWhiteSpace(normalized))
                return BadRequest(new { message = "Invalid email." });

            var duplicate = await _db.Users.AsNoTracking()
                .AnyAsync(u => u.Id != user.Id && u.NormalizedEmail == normalized, default);
            if (duplicate)
                return BadRequest(new { message = "Email is already in use by another user." });

            if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                if (!user.CompanyId.HasValue)
                    return BadRequest(new { message = "Your account is not linked to a company." });
                user.Email = newEmail;
                user.UserName = UserIdentityNames.ScopedUserName(_userManager, user.CompanyId.Value, newEmail);
                changes.Add($"Email: '{beforeEmail}' -> '{newEmail}'");
            }
        }

        if (request.FirstName != null)
        {
            var v = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim();
            if (!string.Equals(beforeFirstName, v, StringComparison.Ordinal))
                changes.Add($"First name: '{beforeFirstName ?? "(empty)"}' -> '{v ?? "(empty)"}'");
            user.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim();
        }
        if (request.LastName != null)
        {
            var v = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();
            if (!string.Equals(beforeLastName, v, StringComparison.Ordinal))
                changes.Add($"Last name: '{beforeLastName ?? "(empty)"}' -> '{v ?? "(empty)"}'");
            user.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();
        }
        if (request.FirstName != null || request.LastName != null)
        {
            var fn = user.FirstName ?? "";
            var ln = user.LastName ?? "";
            user.FullName = string.Join(" ", new[] { fn, ln }.Where(s => !string.IsNullOrEmpty(s))).Trim();
            if (string.IsNullOrEmpty(user.FullName))
                user.FullName = user.Email ?? "";
        }
        if (request.Phone != null)
        {
            var v = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            if (!string.Equals(beforePhone, v, StringComparison.Ordinal))
                changes.Add($"Phone: '{beforePhone ?? "(empty)"}' -> '{v ?? "(empty)"}'");
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(new { message = string.Join(" ", updateResult.Errors.Select(e => e.Description)) });

        if (!string.Equals(beforeFullName, user.FullName, StringComparison.Ordinal))
            changes.Add($"Full name: '{beforeFullName ?? "(empty)"}' -> '{user.FullName ?? "(empty)"}'");

        var roles = await _userManager.GetRolesAsync(user);
        var isClient = roles.Contains(SeedData.RoleClient);
        if (isClient && changes.Count > 0)
        {
            try
            {
                var company = user.CompanyId.HasValue
                    ? await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == user.CompanyId.Value)
                    : null;
                var adminCompanyId = company?.ParentCompanyId ?? user.CompanyId;
                if (adminCompanyId.HasValue)
                {
                    var admins = await _userManager.GetUsersInRoleAsync(SeedData.RoleAdmin);
                    var recipients = admins
                        .Where(a => a.IsActive && a.CompanyId == adminCompanyId && !string.IsNullOrWhiteSpace(a.Email))
                        .Select(a => a.Email!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (recipients.Count > 0)
                    {
                        var companyName = company?.Name ?? "(unknown company)";
                        var allDetails = $"""
Client user details
- User ID: {user.Id}
- Company: {companyName}
- Email: {user.Email ?? "(empty)"}
- Full name: {user.FullName ?? "(empty)"}
- First name: {user.FirstName ?? "(empty)"}
- Last name: {user.LastName ?? "(empty)"}
- Phone: {user.PhoneNumber ?? "(empty)"}
- Active: {user.IsActive}
""";
                        var body = $"""
A client user has updated their profile.

Updated fields:
{string.Join("\n", changes.Select(c => "- " + c))}

{allDetails}
""";
                        foreach (var to in recipients)
                            await _emailService.SendCustomEmailAsync(to, "Client profile updated", body);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send admin notifications for client profile update for user {UserId}", user.Id);
            }
        }

        return Ok(new ProfileDto
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.PhoneNumber
        });
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
            return Unauthorized();

        var checkResult = await _signInManager.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: false);
        if (!checkResult.Succeeded)
            return BadRequest(new { message = "Current password is incorrect." });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!resetResult.Succeeded)
            return BadRequest(new { message = string.Join(" ", resetResult.Errors.Select(e => e.Description)) });

        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var normalized = _userManager.NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(normalized))
            return NoContent();

        var user = await _db.Users
            .Where(u => u.NormalizedEmail == normalized && u.IsActive)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(ct);

        // Avoid user-enumeration by always returning 204.
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
            return NoContent();

        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        var appBaseUrl = (_configuration["App:BaseUrl"] ?? "http://localhost:4300").TrimEnd('/');
        var resetUrl = $"{appBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(encodedToken)}";

        var body = $"""
Hello {(!string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : user.Email)},

We received a request to reset your password.

Reset your password using this secure link:
{resetUrl}

If you did not request this, you can safely ignore this email.

Kind regards,
Da Vinci's Civils & Pumps
""";

        try
        {
            await _db.SaveChangesAsync(ct);
            var subject = "Reset your Tradion password";
            var emailService = HttpContext.RequestServices.GetRequiredService<IEmailService>();
            await emailService.SendCustomEmailAsync(user.Email, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send forgot-password email for {Email}", request.Email);
        }

        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct = default)
    {
        var normalized = _userManager.NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(normalized))
            return BadRequest(new { message = "Invalid reset request." });

        var user = await _db.Users
            .Where(u => u.NormalizedEmail == normalized && u.IsActive)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(ct);
        if (user == null)
            return BadRequest(new { message = "Invalid reset request." });

        string decodedToken;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(request.Token);
            decodedToken = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return BadRequest(new { message = "Invalid or expired reset token." });
        }

        var result = await _userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        return NoContent();
    }
}
