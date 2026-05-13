namespace Tradion.Api.Permits;

/// <summary>
/// In-code definition for a DVCP-style permit type and its default checklist (flat id/label items for API/mobile).
/// The Work Authorisation master uses <see cref="UsesStructuredWorkAuthorisationForm"/>; full form data stays in Work Authorisation JSON.
/// </summary>
public interface IPermitCatalogDefinition
{
    /// <summary>Stable code key (e.g. for logs or future DB column); not stored on <see cref="Models.PermitType"/> today.</summary>
    string StableKey { get; }

    /// <summary>Display name; must match <see cref="Models.PermitType.Name"/> for idempotent seeding.</summary>
    string Name { get; }

    /// <summary>Optional short summary (motto / rule headline).</summary>
    string? Description { get; }

    bool IsWorkAuthorisation { get; }

    /// <summary>True only for the master: UI uses Work Authorisation flow instead of checklist template alone.</summary>
    bool UsesStructuredWorkAuthorisationForm { get; }

    IReadOnlyList<PermitChecklistLine> ChecklistLines { get; }

    /// <summary>Structured fields for child permits; empty for WA (uses Work Authorisation DTO).</summary>
    IReadOnlyList<PermitFormFieldDefinition> FormFields { get; }
}

public sealed record PermitChecklistLine(string Id, string Label);
