namespace Ike.Api.Models;

public class ServiceRequestAttachment
{
    public Guid Id { get; set; }
    public Guid ServiceRequestId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ServiceRequest ServiceRequest { get; set; } = null!;
}
