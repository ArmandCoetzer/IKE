namespace Tradion.Api.DTOs.PermitTypes;

public class PermitTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsWorkAuthorisation { get; set; }
    public string? TriggersPermitTypeIdsJson { get; set; }
}

public class CreatePermitTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsWorkAuthorisation { get; set; }
    public string? TriggersPermitTypeIdsJson { get; set; }
}
