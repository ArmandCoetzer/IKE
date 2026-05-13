namespace Tradion.Api.DTOs.JobCardWork;

public class PermitFormFieldSchemaDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string? Group { get; set; }
    public bool Required { get; set; }
}
