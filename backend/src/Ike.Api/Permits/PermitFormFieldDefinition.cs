namespace Ike.Api.Permits;

/// <summary>Declarative field for child (non–Work Authorisation) permit forms. Serialized to <see cref="Models.PermitTemplate.FormSchemaJson"/>.</summary>
public sealed record PermitFormFieldDefinition(
    string Id,
    string Label,
    string Type = "text",
    string? Group = null,
    bool Required = false)
{
    public const string TypeText = "text";
    public const string TypeTextArea = "textarea";
    public const string TypeBool = "bool";
    public const string TypeDate = "date";
}
