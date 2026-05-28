namespace Ike.Api.Models;

public class AuditErrorEntry
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? TraceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
