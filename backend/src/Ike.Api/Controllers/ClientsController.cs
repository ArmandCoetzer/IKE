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
    private static readonly string[] ClientImportHeaders =
    {
        "Company Name", "Contact Name", "Phone", "Email", "Site Name", "Site Address"
    };

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

    [HttpGet("import-template")]
    [Authorize(Policy = "RequireEditClients")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public ActionResult DownloadImportTemplate()
    {
        var xlsx = ImportFileHelper.CreateXlsxTemplate(ClientImportHeaders, "Clients");
        return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "client-import-template.xlsx");
    }

    [HttpPost("import-preview")]
    [Authorize(Policy = "RequireEditClients")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ClientImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientImportResultDto>> ImportPreview([FromForm] IFormFile? file, CancellationToken ct = default)
    {
        var parentCompanyId = await GetCurrentCompanyIdAsync();
        if (!parentCompanyId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Clients can only be imported by users with a company."));

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponseBodies.Message("Select an XLSX file to import."));
        var ext = Path.GetExtension(file.FileName);
        if (!ImportFileHelper.AllowedExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Only XLSX files are supported."));

        var rows = (await ImportFileHelper.ReadRowsAsync(file, ct)).Select(ToClientImportRow).ToList();
        await ValidateClientImportRowsAsync(rows, parentCompanyId.Value, ct);
        return Ok(ToClientImportResult(rows, Array.Empty<ClientImportRowDto>()));
    }

    [HttpPost("import-commit")]
    [Authorize(Policy = "RequireEditClients")]
    [ProducesResponseType(typeof(ClientImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientImportResultDto>> ImportCommit([FromBody] ClientImportCommitRequest request, CancellationToken ct = default)
    {
        var parentCompanyId = await GetCurrentCompanyIdAsync();
        if (!parentCompanyId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Clients can only be imported by users with a company."));

        var rows = request.Rows.Select(CloneClientImportRow).ToList();
        await ValidateClientImportRowsAsync(rows, parentCompanyId.Value, ct);
        var failedRows = rows.Where(r => r.Errors.Count > 0).ToList();
        var succeededRows = new List<ClientImportRowDto>();

        foreach (var group in rows.Where(r => r.Errors.Count == 0).GroupBy(r => NormalizeImportKey(r.CompanyName)))
        {
            var groupRows = group.ToList();
            if (groupRows.Count == 0)
                continue;

            var first = groupRows[0];
            var company = new Company
            {
                Id = Guid.NewGuid(),
                Name = first.CompanyName.Trim(),
                Type = CompanyType.Client,
                ParentCompanyId = parentCompanyId,
                ContactEmail = string.IsNullOrWhiteSpace(first.Email) ? null : first.Email.Trim(),
                ContactPhone = string.IsNullOrWhiteSpace(first.Phone) ? null : first.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(first.ContactName) ? null : first.ContactName.Trim(),
                IsActive = true
            };
            _db.Companies.Add(company);

            string? inviteToken = null;
            ApplicationUser? newPortalUser = null;
            var clientEmail = company.ContactEmail;
            if (!string.IsNullOrEmpty(clientEmail))
            {
                inviteToken = Guid.NewGuid().ToString("N");
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
                    InviteTokenExpiry = DateTime.UtcNow.AddDays(7)
                };
                var createResult = await _userManager.CreateAsync(user, tempPassword);
                if (!createResult.Succeeded)
                {
                    var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
                    foreach (var row in groupRows)
                    {
                        row.Errors.Add(message);
                        failedRows.Add(row);
                    }
                    _db.Companies.Remove(company);
                    continue;
                }
                await _userManager.AddToRoleAsync(user, SeedData.RoleClient);
                newPortalUser = user;
            }

            foreach (var row in groupRows)
            {
                var site = new Site
                {
                    Id = Guid.NewGuid(),
                    Name = row.SiteName.Trim(),
                    Address = string.IsNullOrWhiteSpace(row.SiteAddress) ? null : row.SiteAddress.Trim(),
                    CompanyId = company.Id,
                    IsActive = true
                };
                _db.Sites.Add(site);
                row.CreatedClientId = company.Id;
                row.CreatedSiteId = site.Id;
                succeededRows.Add(row);
            }

            await _db.SaveChangesAsync(ct);
            if (!string.IsNullOrEmpty(clientEmail) && !string.IsNullOrEmpty(inviteToken))
            {
                var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:4201";
                var inviteLink = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(inviteToken)}";
                await _emailService.SendClientInviteEmailAsync(clientEmail, inviteLink, company.Name);
            }
        }

        return Ok(ToClientImportResult(succeededRows, failedRows));
    }

    private async Task<Guid?> GetCurrentCompanyIdAsync()
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUser = currentUserId != null ? await _userManager.FindByIdAsync(currentUserId) : null;
        return currentUser?.CompanyId;
    }

    private static ClientImportRowDto ToClientImportRow(ImportTableRow row)
    {
        string Value(params string[] names)
        {
            foreach (var name in names)
            {
                if (row.Values.TryGetValue(ImportFileHelper.NormalizeHeader(name), out var value))
                    return value.Trim();
            }
            return string.Empty;
        }

        return new ClientImportRowDto
        {
            RowNumber = row.RowNumber,
            CompanyName = Value("Company Name", "Company", "Client Name"),
            ContactName = EmptyToNull(Value("Contact Name", "Contact")),
            Phone = EmptyToNull(Value("Phone", "Contact Phone")),
            Email = EmptyToNull(Value("Email", "Contact Email")),
            SiteName = Value("Site Name", "Site"),
            SiteAddress = EmptyToNull(Value("Site Address", "Address"))
        };
    }

    private static ClientImportRowDto CloneClientImportRow(ClientImportRowDto row) => new()
    {
        RowNumber = row.RowNumber,
        CompanyName = row.CompanyName?.Trim() ?? string.Empty,
        ContactName = EmptyToNull(row.ContactName),
        Phone = EmptyToNull(row.Phone),
        Email = EmptyToNull(row.Email),
        SiteName = row.SiteName?.Trim() ?? string.Empty,
        SiteAddress = EmptyToNull(row.SiteAddress)
    };

    private async Task ValidateClientImportRowsAsync(List<ClientImportRowDto> rows, Guid parentCompanyId, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            row.Errors.Clear();
            row.CompanyName = row.CompanyName?.Trim() ?? string.Empty;
            row.SiteName = row.SiteName?.Trim() ?? string.Empty;
            row.ContactName = EmptyToNull(row.ContactName);
            row.Phone = EmptyToNull(row.Phone);
            row.Email = EmptyToNull(row.Email);
            row.SiteAddress = EmptyToNull(row.SiteAddress);

            if (string.IsNullOrWhiteSpace(row.CompanyName))
                row.Errors.Add("Company name is required.");
            if (string.IsNullOrWhiteSpace(row.SiteName))
                row.Errors.Add("Site name is required.");
            if (!string.IsNullOrWhiteSpace(row.Email) && !IsLikelyEmail(row.Email))
                row.Errors.Add("Email address is not valid.");
        }

        var existingClientNames = await _db.Companies.AsNoTracking()
            .Where(c => c.Type == CompanyType.Client && c.ParentCompanyId == parentCompanyId)
            .Select(c => c.Name)
            .ToListAsync(ct);
        var existingClientNameKeys = existingClientNames.Select(NormalizeImportKey).ToHashSet();

        foreach (var clientGroup in rows.Where(r => !string.IsNullOrWhiteSpace(r.CompanyName)).GroupBy(r => NormalizeImportKey(r.CompanyName)))
        {
            var groupRows = clientGroup.ToList();
            if (existingClientNameKeys.Contains(clientGroup.Key))
            {
                foreach (var row in groupRows)
                    row.Errors.Add("Client already exists.");
            }

            var first = groupRows[0];
            if (groupRows.Any(r => !SameOptionalValue(r.Email, first.Email)
                                   || !SameOptionalValue(r.Phone, first.Phone)
                                   || !SameOptionalValue(r.ContactName, first.ContactName)))
            {
                foreach (var row in groupRows)
                    row.Errors.Add("Client details differ across rows for the same company.");
            }

            foreach (var siteGroup in groupRows.Where(r => !string.IsNullOrWhiteSpace(r.SiteName)).GroupBy(r => NormalizeImportKey(r.SiteName)))
            {
                if (siteGroup.Count() <= 1)
                    continue;
                foreach (var row in siteGroup)
                    row.Errors.Add("Duplicate site for this client in the import file.");
            }
        }
    }

    private static ClientImportResultDto ToClientImportResult(IEnumerable<ClientImportRowDto> successRows, IEnumerable<ClientImportRowDto> failedRows)
    {
        var successes = successRows.ToList();
        var failures = failedRows.ToList();
        var rows = successes.Concat(failures).OrderBy(r => r.RowNumber).ToList();
        return new ClientImportResultDto
        {
            Rows = rows,
            FailedRows = failures.OrderBy(r => r.RowNumber).ToList(),
            TotalRows = rows.Count,
            SuccessCount = successes.Count,
            FailedCount = failures.Count
        };
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsLikelyEmail(string value) =>
        value.Contains('@') && value.Contains('.') && !value.Any(char.IsWhiteSpace);

    private static string NormalizeImportKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool SameOptionalValue(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

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
