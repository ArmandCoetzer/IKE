using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Invite;
using Ike.Api.Models;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class InviteController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public InviteController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet("invite-info")]
    [ProducesResponseType(typeof(InviteInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InviteInfoDto>> GetInviteInfo([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Token is required." });
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.InviteToken == token);
        if (user == null)
            return NotFound(new { message = "Invalid or expired link." });
        if (user.InviteTokenExpiry == null || user.InviteTokenExpiry.Value < DateTime.UtcNow)
            return BadRequest(new { message = "This link has expired." });
        var roles = await _userManager.GetRolesAsync(user);
        var isClient = roles.Contains(SeedData.RoleClient);
        return Ok(new InviteInfoDto
        {
            Type = isClient ? "client" : "employee",
            Email = user.Email ?? ""
        });
    }

    [HttpPost("complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CompleteInvite([FromBody] CompleteInviteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { message = "Token is required." });
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password != request.ConfirmPassword)
            return BadRequest(new { message = "Password and confirm password must match and be at least 8 characters." });
        if (request.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.InviteToken == request.Token);
        if (user == null)
            return NotFound(new { message = "Invalid or expired link." });
        if (user.InviteTokenExpiry == null || user.InviteTokenExpiry.Value < DateTime.UtcNow)
            return BadRequest(new { message = "This link has expired." });

        var roles = await _userManager.GetRolesAsync(user);
        var isClient = roles.Contains(SeedData.RoleClient);

        if (isClient)
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
                return BadRequest(new { message = "First name and last name are required." });
            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName.Trim();
            user.FullName = $"{user.FirstName} {user.LastName}".Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        }

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.Password!);
        if (!resetResult.Succeeded)
            return BadRequest(new { message = string.Join(" ", resetResult.Errors.Select(e => e.Description)) });

        user.RegistrationStatus = SeedData.RegistrationStatusRegistered;
        user.InviteToken = null;
        user.InviteTokenExpiry = null;
        await _userManager.UpdateAsync(user);

        return Ok(new { message = "Registration complete. You can now log in." });
    }
}
