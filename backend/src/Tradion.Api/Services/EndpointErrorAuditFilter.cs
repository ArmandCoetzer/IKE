using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Tradion.Api.Data;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

/// <summary>
/// Global endpoint-level error capture.
/// Logs handled 4xx/5xx results and unhandled exceptions into AuditErrorEntries.
/// </summary>
public class EndpointErrorAuditFilter : IAsyncResultFilter, IAsyncExceptionFilter
{
    private readonly ApplicationDbContext _db;
    private readonly IHostEnvironment _environment;
    private const int MaxMessageLength = 2000;
    private const int MaxDetailsLength = 8000;

    public EndpointErrorAuditFilter(ApplicationDbContext db, IHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var statusCode = ResolveStatusCode(context.Result);
        if (statusCode >= 400)
        {
            var message = $"HTTP {statusCode} returned by endpoint.";
            var details = RedactDetails(ResolveResultDetails(context.Result));
            await LogAsync(context.HttpContext, statusCode, message, details);
        }
        await next();
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        await LogAsync(
            context.HttpContext,
            StatusCodes.Status500InternalServerError,
            RedactExceptionMessage(context.Exception.Message),
            RedactDetails(context.Exception.ToString()));
    }

    private static int ResolveStatusCode(IActionResult result) =>
        result switch
        {
            ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult s => s.StatusCode,
            _ => StatusCodes.Status200OK
        };

    private static string? ResolveResultDetails(IActionResult result)
    {
        if (result is ObjectResult o && o.Value != null)
            return o.Value.ToString();
        return null;
    }

    private string RedactExceptionMessage(string message) =>
        _environment.IsProduction() ? "An error occurred." : message;

    private string? RedactDetails(string? details) =>
        _environment.IsProduction() ? null : details;

    private async Task LogAsync(HttpContext http, int statusCode, string message, string? details)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        _db.AuditErrorEntries.Add(new AuditErrorEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Method = Truncate(http.Request.Method, 16),
            Path = Truncate(http.Request.Path.Value ?? string.Empty, 512),
            StatusCode = statusCode,
            Message = Truncate(message, MaxMessageLength),
            Details = string.IsNullOrWhiteSpace(details) ? null : Truncate(details, MaxDetailsLength),
            TraceId = Truncate(http.TraceIdentifier, 128),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
