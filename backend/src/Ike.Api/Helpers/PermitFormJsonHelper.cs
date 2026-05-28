using System.Text.Json;
using Ike.Api.DTOs.JobCardWork;
using Ike.Api.Permits;

namespace Ike.Api.Helpers;

public static class PermitFormJsonHelper
{
    private static readonly JsonSerializerOptions JsonWrite = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

    public static string? SerializeSchema(IReadOnlyList<PermitFormFieldDefinition> fields)
    {
        if (fields.Count == 0) return null;
        var payload = fields.Select(f => new PermitFormFieldSchemaDto
        {
            Id = f.Id,
            Label = f.Label,
            Type = f.Type,
            Group = f.Group,
            Required = f.Required
        }).ToList();
        return JsonSerializer.Serialize(payload, JsonWrite);
    }

    public static List<PermitFormFieldSchemaDto> ParseSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<PermitFormFieldSchemaDto>();
        try
        {
            return JsonSerializer.Deserialize<List<PermitFormFieldSchemaDto>>(json, JsonRead) ?? new List<PermitFormFieldSchemaDto>();
        }
        catch
        {
            return new List<PermitFormFieldSchemaDto>();
        }
    }

    public static Dictionary<string, string> ParseValues(string? json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return dict;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return dict;
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.ValueKind == JsonValueKind.True ? "true"
                    : p.Value.ValueKind == JsonValueKind.False ? "false"
                    : p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "")
                    : p.Value.GetRawText();
        }
        catch
        {
            // ignore
        }

        return dict;
    }

    public static string? SerializeValues(Dictionary<string, string?>? form)
    {
        if (form == null || form.Count == 0) return null;
        var clean = form.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(clean, JsonWrite);
    }

    /// <summary>Template checklist lines from <see cref="Models.PermitTemplate.ChecklistJson"/>.</summary>
    public static List<(string Id, string Label)> ParseChecklistTemplate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<(string, string)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var label = e.TryGetProperty("label", out var lbProp) ? lbProp.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(label))
                    list.Add((id, label));
            }

            return list;
        }
        catch
        {
            return new List<(string, string)>();
        }
    }

    public static bool IsTruthy(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return false;
        var s = v.Trim();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase)
               || s == "1"
               || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || s.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
