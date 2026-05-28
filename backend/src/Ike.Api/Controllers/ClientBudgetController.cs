using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.ClientBudget;
using Ike.Api.Models;
using Ike.Api.Services;


namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClientBudgetController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;

    public ClientBudgetController(ApplicationDbContext db, ICurrentUserService currentUser, IAuditService auditService)
    {
        _db = db;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    /// <summary>Get budget for a specific client company. Admin only; company must be a child of current user's company.</summary>
    [HttpGet("for-company/{companyId:guid}")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(ClientBudgetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientBudgetDto>> GetForCompany(Guid companyId, CancellationToken ct = default)
    {
        var (myCompanyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (isClient)
            return NotFound();
        if (!myCompanyId.HasValue)
            return NotFound();
        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId && c.Type == CompanyType.Client && c.ParentCompanyId == myCompanyId.Value, ct);
        if (company == null)
            return NotFound();
        var budget = await _db.ClientBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId, ct);
        if (budget == null)
            return Ok(new ClientBudgetDto
            {
                Id = Guid.Empty,
                CompanyId = companyId,
                ThresholdAmount = 0,
                SpentAmount = 0,
                Currency = "ZAR",
                WorkPaused = false
            });
        return Ok(new ClientBudgetDto
        {
            Id = budget.Id,
            CompanyId = budget.CompanyId,
            ThresholdAmount = budget.ThresholdAmount,
            SpentAmount = budget.SpentAmount,
            Currency = budget.Currency,
            WorkPaused = budget.WorkPaused,
            PausedAt = budget.PausedAt,
            ContinuationApprovedAt = budget.ContinuationApprovedAt
        });
    }

    /// <summary>Get budget for current user's company. Clients only see their own.</summary>
    [HttpGet]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(ClientBudgetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientBudgetDto>> GetMyBudget(CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return NotFound();
        var budget = await _db.ClientBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId.Value, ct);
        if (budget == null)
            return Ok(new ClientBudgetDto
            {
                Id = Guid.Empty,
                CompanyId = companyId.Value,
                ThresholdAmount = 0,
                SpentAmount = 0,
                Currency = "ZAR",
                WorkPaused = false
            });
        return Ok(new ClientBudgetDto
        {
            Id = budget.Id,
            CompanyId = budget.CompanyId,
            ThresholdAmount = budget.ThresholdAmount,
            SpentAmount = budget.SpentAmount,
            Currency = budget.Currency,
            WorkPaused = budget.WorkPaused,
            PausedAt = budget.PausedAt,
            ContinuationApprovedAt = budget.ContinuationApprovedAt
        });
    }

    /// <summary>Client approves continuation when work is paused (budget threshold exceeded).</summary>
    [HttpPost("approve-continuation")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(ClientBudgetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientBudgetDto>> ApproveContinuation(CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return NotFound();
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        var budget = await _db.ClientBudgets.FirstOrDefaultAsync(b => b.CompanyId == companyId.Value, ct);
        if (budget == null)
            return NotFound();
        if (!budget.WorkPaused)
            return Ok(new ClientBudgetDto
            {
                Id = budget.Id,
                CompanyId = budget.CompanyId,
                ThresholdAmount = budget.ThresholdAmount,
                SpentAmount = budget.SpentAmount,
                Currency = budget.Currency,
                WorkPaused = false,
                PausedAt = budget.PausedAt,
                ContinuationApprovedAt = budget.ContinuationApprovedAt
            });
        budget.WorkPaused = false;
        budget.ContinuationApprovedAt = DateTime.UtcNow;
        budget.ContinuationApprovedByUserId = userId;
        budget.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("BudgetContinuationApproved", "ClientBudget", budget.Id.ToString(), $"CompanyId: {budget.CompanyId}", ct);
        return Ok(new ClientBudgetDto
        {
            Id = budget.Id,
            CompanyId = budget.CompanyId,
            ThresholdAmount = budget.ThresholdAmount,
            SpentAmount = budget.SpentAmount,
            Currency = budget.Currency,
            WorkPaused = false,
            PausedAt = budget.PausedAt,
            ContinuationApprovedAt = budget.ContinuationApprovedAt
        });
    }
}
