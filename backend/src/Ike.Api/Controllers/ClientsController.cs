using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Clients;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;

    public ClientsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailService emailService, IConfiguration configuration)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
        _configuration = configuration;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewClients")]
    [ProducesResponseType(typeof(List<ClientDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ClientDto>>> List([FromQuery] bool? isActive)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var myCompanyId = currentUser?.CompanyId;

        IQueryable<Company> query = _db.Companies.AsNoTracking()
            .Where(c => c.Type == CompanyType.Client);
        if (myCompanyId.HasValue)
            query = query.Where(c => c.ParentCompanyId == myCompanyId);
        if (isActive.HasValue)
            query = query.Where(c => c.IsActive == isActive.Value);

        var companies = await query
            .OrderBy(c => c.Name)
            .ToListAsync();

        var result = new List<ClientDto>();
        foreach (var company in companies)
        {
            var clientUser = await _userManager.Users
                .AsNoTracking()
                .Where(u => u.CompanyId == company.Id)
                .FirstOrDefaultAsync();
            var roles = clientUser != null ? await _userManager.GetRolesAsync(clientUser) : Array.Empty<string>();
            var isClientUser = roles.Contains(SeedData.RoleClient);

            result.Add(new ClientDto
            {
                Id = company.Id,
                CompanyName = company.Name,
                ContactName = company.Address,
                Phone = company.ContactPhone,
                Email = company.ContactEmail,
                UserId = isClientUser ? clientUser!.Id : null,
                UserEmail = isClientUser ? clientUser!.Email : null,
                IsActive = company.IsActive,
                CreatedAt = company.CreatedAt
            });
        }
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewClients")]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDto>> Get(Guid id)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var myCompanyId = currentUser?.CompanyId;

        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == CompanyType.Client);
        if (company == null)
            return NotFound();
        if (myCompanyId.HasValue && company.ParentCompanyId != myCompanyId)
            return NotFound();

        var clientUser = await _userManager.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == company.Id)
            .FirstOrDefaultAsync();
        var roles = clientUser != null ? await _userManager.GetRolesAsync(clientUser) : Array.Empty<string>();
        var isClientUser = roles.Contains(SeedData.RoleClient);

        return Ok(new ClientDto
        {
            Id = company.Id,
            CompanyName = company.Name,
            ContactName = company.Address,
            Phone = company.ContactPhone,
            Email = company.ContactEmail,
            UserId = isClientUser ? clientUser!.Id : null,
            UserEmail = isClientUser ? clientUser!.Email : null,
            IsActive = company.IsActive,
            CreatedAt = company.CreatedAt
        });
    }

    [HttpGet("{id:guid}/portal-users")]
    [Authorize(Policy = "RequireViewClients")]
    [ProducesResponseType(typeof(List<ClientPortalUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ClientPortalUserDto>>> GetPortalUsers(Guid id, CancellationToken ct = default)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var myCompanyId = currentUser?.CompanyId;

        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == CompanyType.Client, ct);
        if (company == null)
            return NotFound();
        if (myCompanyId.HasValue && company.ParentCompanyId != myCompanyId)
            return NotFound();

        var users = await _userManager.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == id)
            .ToListAsync(ct);

        var result = new List<ClientPortalUserDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            if (!roles.Contains(SeedData.RoleClient))
                continue;

            result.Add(new ClientPortalUserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FullName = u.FullName,
                IsActive = u.IsActive,
                RegistrationStatus = u.RegistrationStatus
            });
        }

        return Ok(result.OrderBy(x => x.Email).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "RequireEditClients")]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientDto>> Create([FromBody] CreateClientRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            return BadRequest(ApiResponseBodies.Message("Company name is required."));
        var clientEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var parentCompanyId = currentUser?.CompanyId;

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName.Trim(),
            Type = CompanyType.Client,
            ParentCompanyId = parentCompanyId,
            ContactEmail = clientEmail,
            ContactPhone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Address = string.IsNullOrWhiteSpace(request.ContactName) ? null : request.ContactName.Trim(),
            IsActive = true
        };
        _db.Companies.Add(company);

        string? inviteToken = null;
        ApplicationUser? newPortalUser = null;
        bool? portalUserCreated = null;
        string? portalMessage = null;

        if (!string.IsNullOrEmpty(clientEmail))
        {
            portalUserCreated = true;
            inviteToken = Guid.NewGuid().ToString("N");
            var expiry = DateTime.UtcNow.AddDays(7);
            var tempPassword = "Temp1!" + Guid.NewGuid().ToString("N")[..20];
            var user = new ApplicationUser
            {
                UserName = UserIdentityNames.ScopedUserName(_userManager, company.Id, clientEmail),
                Email = clientEmail,
                EmailConfirmed = true,
                FullName = "",
                FirstName = null,
                LastName = null,
                CompanyId = company.Id,
                RegistrationStatus = SeedData.RegistrationStatusInvited,
                InviteToken = inviteToken,
                InviteTokenExpiry = expiry
            };
            var createResult = await _userManager.CreateAsync(user, tempPassword);
            if (!createResult.Succeeded)
                return BadRequest(new { message = string.Join(" ", createResult.Errors.Select(e => e.Description)) });
            await _userManager.AddToRoleAsync(user, SeedData.RoleClient);
            newPortalUser = user;
        }

        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(clientEmail) && !string.IsNullOrEmpty(inviteToken))
        {
            var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:4201";
            var inviteLink = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(inviteToken)}";
            await _emailService.SendClientInviteEmailAsync(clientEmail, inviteLink, company.Name);
        }

        return CreatedAtAction(nameof(Get), new { id = company.Id }, new ClientDto
        {
            Id = company.Id,
            CompanyName = company.Name,
            ContactName = company.Address,
            Phone = company.ContactPhone,
            Email = company.ContactEmail,
            UserId = newPortalUser?.Id,
            UserEmail = newPortalUser?.Email,
            PortalUserCreated = portalUserCreated,
            PortalMessage = portalMessage,
            IsActive = company.IsActive,
            CreatedAt = company.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireEditClients")]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDto>> Update(Guid id, [FromBody] UpdateClientRequest request)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var myCompanyId = currentUser?.CompanyId;

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id && c.Type == CompanyType.Client);
        if (company == null)
            return NotFound();
        if (myCompanyId.HasValue && company.ParentCompanyId != myCompanyId)
            return NotFound();
        if (request.CompanyName != null) company.Name = request.CompanyName.Trim();
        if (request.ContactName != null) company.Address = string.IsNullOrWhiteSpace(request.ContactName) ? null : request.ContactName.Trim();
        if (request.Phone != null) company.ContactPhone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (request.Email != null) company.ContactEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (request.IsActive.HasValue) company.IsActive = request.IsActive.Value;
        company.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var clientUser = await _userManager.Users.Where(u => u.CompanyId == company.Id).FirstOrDefaultAsync();
        var roles = clientUser != null ? await _userManager.GetRolesAsync(clientUser) : Array.Empty<string>();
        var isClientUser = roles.Contains(SeedData.RoleClient);

        return Ok(new ClientDto
        {
            Id = company.Id,
            CompanyName = company.Name,
            ContactName = company.Address,
            Phone = company.ContactPhone,
            Email = company.ContactEmail,
            UserId = isClientUser ? clientUser!.Id : null,
            UserEmail = clientUser?.Email,
            IsActive = company.IsActive,
            CreatedAt = company.CreatedAt
        });
    }

    [HttpPost("{id:guid}/portal-users/{userId}/re-invite")]
    [Authorize(Policy = "RequireEditClients")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ReInvitePortalUser(Guid id, string userId, CancellationToken ct = default)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var myCompanyId = currentUser?.CompanyId;

        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == CompanyType.Client, ct);
        if (company == null)
            return NotFound();
        if (myCompanyId.HasValue && company.ParentCompanyId != myCompanyId)
            return NotFound();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.CompanyId != id)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(SeedData.RoleClient))
            return NotFound();
        if (string.IsNullOrWhiteSpace(user.Email))
            return BadRequest(new { message = "User has no email address." });

        user.RegistrationStatus = SeedData.RegistrationStatusInvited;
        user.InviteToken = Guid.NewGuid().ToString("N");
        user.InviteTokenExpiry = DateTime.UtcNow.AddDays(7);

        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return BadRequest(new { message = string.Join(" ", update.Errors.Select(e => e.Description)) });

        var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:4201";
        var inviteLink = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(user.InviteToken)}";
        await _emailService.SendClientInviteEmailAsync(user.Email, inviteLink, company.Name, ct);

        return Ok(new { message = "Client portal invite sent." });
    }

    [HttpPut("{id:guid}/portal-users/{userId}/status")]
    [Authorize(Policy = "RequireEditClients")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientPortalUserDto>> SetPortalUserStatus(Guid id, string userId, [FromBody] SetClientPortalUserStatusRequest request, CancellationToken ct = default)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        var myCompanyId = currentUser?.CompanyId;

        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == CompanyType.Client, ct);
        if (company == null)
            return NotFound();
        if (myCompanyId.HasValue && company.ParentCompanyId != myCompanyId)
            return NotFound();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.CompanyId != id)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(SeedData.RoleClient))
            return NotFound();

        user.IsActive = request.IsActive;
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return BadRequest(new { message = string.Join(" ", update.Errors.Select(e => e.Description)) });

        return Ok(new ClientPortalUserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            IsActive = user.IsActive,
            RegistrationStatus = user.RegistrationStatus
        });
    }
}
