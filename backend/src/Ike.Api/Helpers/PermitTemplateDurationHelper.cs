using Ike.Api.Models;

namespace Ike.Api.Helpers;

/// <summary>Display label and default validity windows; optional override via <see cref="PermitTemplate.ValidityRulesJson"/> (<c>hours</c>).</summary>
public static class PermitTemplateDurationHelper
{
    /// <summary>List/card title: prefer <see cref="PermitType.Name"/> over template row name (e.g. avoid showing &quot;Default&quot;).</summary>
    public static string? PrimaryDisplayName(PermitTemplate? template) =>
        !string.IsNullOrWhiteSpace(template?.PermitType?.Name) ? template!.PermitType!.Name : template?.Name;

    public static double ResolvePermitDurationHours(PermitTemplate? template)
    {
        // Work Authorisation: 5 days. Hot Work: 12 h (half day). Others: 24 h unless JSON overrides.
        var typeName = (template?.PermitType?.Name ?? string.Empty).ToLowerInvariant();
        var hours = template?.PermitType?.IsWorkAuthorisation == true ? 120.0
            : typeName.Contains("hot") && typeName.Contains("work") ? 12.0
            : 24.0;
        if (!string.IsNullOrWhiteSpace(template?.ValidityRulesJson))
        {
            try
            {
                var rules = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(template.ValidityRulesJson);
                if (rules.TryGetProperty("hours", out var h) && h.TryGetDouble(out var hr))
                    hours = Math.Clamp(hr, 0.5, 240);
            }
            catch
            {
                // Use defaults above.
            }
        }

        return hours;
    }
}
