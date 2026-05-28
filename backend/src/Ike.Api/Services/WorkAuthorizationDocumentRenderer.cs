using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ike.Api.DTOs.WorkAuthorizations;

namespace Ike.Api.Services;

public class WorkAuthorizationDocumentRenderer : IWorkAuthorizationDocumentRenderer
{
    private readonly IWorkAuthorizationPermitRulesService _rules;
    private readonly IWebHostEnvironment _env;

    public WorkAuthorizationDocumentRenderer(IWorkAuthorizationPermitRulesService rules, IWebHostEnvironment env)
    {
        _rules = rules;
        _env = env;
    }

    public string RenderHtml(WorkAuthorizationMasterPermitDto permit)
    {
        permit.DerivedRequiredPermits = _rules.GetDerivedPermits(permit);
        var vm = new WorkAuthorizationDocumentViewModel
        {
            Permit = permit,
            DerivedPermits = permit.DerivedRequiredPermits,
            RenderedAtUtc = DateTime.UtcNow
        };

        var sb = new StringBuilder();
        var logoBase64 = PdfTheme.LoadPrimaryLogoBase64(_env);
        sb.Append("""
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Work Authorisation Permit</title>
  <style>
    body{font-family:Arial,Helvetica,sans-serif;background:#f5f7fb;color:#1f2937;margin:0;padding:24px;}
    .doc{max-width:1080px;margin:0 auto;background:#fff;border:1px solid #dbe2ea;border-radius:12px;padding:24px;}
    h1{margin:0 0 6px;font-size:28px} h2{margin:0 0 12px;font-size:18px}
    .muted{color:#6b7280;font-size:12px} .section{border:1px solid #e5e7eb;border-radius:10px;padding:14px;margin-top:14px}
    .grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px} .kv{background:#f9fafb;border:1px solid #eef2f7;border-radius:8px;padding:8px}
    .k{font-size:11px;color:#6b7280;text-transform:uppercase} .v{font-size:14px;font-weight:600;white-space:pre-wrap}
    .chips{display:flex;flex-wrap:wrap;gap:8px} .chip{font-size:12px;padding:4px 8px;border-radius:999px;background:#ecfeff;border:1px solid #bae6fd}
    table{width:100%;border-collapse:collapse} th,td{border:1px solid #e5e7eb;padding:7px;font-size:12px;text-align:left}
    .sig{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px} .sigbox{border:1px solid #e5e7eb;border-radius:8px;padding:10px;min-height:120px}
    .sigimg{max-width:100%;max-height:90px;display:block;border:1px dashed #cbd5e1;border-radius:4px}
    @media print{body{background:#fff;padding:0}.doc{border:none;max-width:none;border-radius:0;padding:0}.section{break-inside:avoid}}
  </style>
</head>
<body>
<div class="doc">
""");

        if (!string.IsNullOrWhiteSpace(logoBase64))
            sb.Append($"<img src=\"data:image/png;base64,{logoBase64}\" alt=\"Logo\" style=\"max-height:130px;width:auto;display:block;margin-bottom:8px;\" />");
        sb.Append($"<h1>WORK AUTHORISATION (MASTER PERMIT) {E(vm.Permit.WorkAuthorizationNumber)}</h1>");
        sb.Append($"<div class=\"muted\">Permit ID: {vm.Permit.PermitGuid} | Generated: {vm.RenderedAtUtc:yyyy-MM-dd HH:mm} UTC</div>");

        RenderKeyValues(sb, "Header", new Dictionary<string, string?>
        {
            ["Issue date"] = FDate(vm.Permit.Header.IssueDate),
            ["Valid from date"] = FDate(vm.Permit.Header.ValidFromDate),
            ["Valid to date"] = FDate(vm.Permit.Header.ValidToDate),
            ["Valid from time"] = FTime(vm.Permit.Header.ValidFromTime),
            ["Valid to time"] = FTime(vm.Permit.Header.ValidToTime),
            ["Workers"] = vm.Permit.Header.NumberOfWorkers?.ToString(),
            ["Site"] = vm.Permit.Header.SiteName,
            ["Scope"] = vm.Permit.Header.ScopeOfWorks,
            ["ATEX zone"] = vm.Permit.Header.AtexZone
        });

        RenderKeyValues(sb, "Location & Task", new Dictionary<string, string?>
        {
            ["Contractor"] = vm.Permit.LocationTask.ContractorCompany,
            ["Location"] = vm.Permit.LocationTask.LocationOfOperation,
            ["Task"] = vm.Permit.LocationTask.TaskDescription
        });

        sb.Append("<div class=\"section\"><h2>Associated Permits</h2>");
        sb.Append($"<div class=\"muted\">Project: {YN(vm.Permit.AssociatedPermits.IsProject)} | Maintenance: {YN(vm.Permit.AssociatedPermits.IsMaintenance)} | Safety induction completed: {YN(vm.Permit.AssociatedPermits.SafetyConditionCompleted)}</div>");
        if (!string.IsNullOrWhiteSpace(vm.Permit.AssociatedPermits.PreventionPlanReferenceNumber))
            sb.Append($"<p><strong>Prevention plan ref:</strong> {E(vm.Permit.AssociatedPermits.PreventionPlanReferenceNumber)}</p>");
        sb.Append("<table><thead><tr><th>Permit</th><th>Required</th><th>Reference</th><th>Notes</th></tr></thead><tbody>");
        foreach (var p in vm.Permit.AssociatedPermits.Permits)
            sb.Append($"<tr><td>{E(p.PermitLabel)}</td><td>{YN(p.IsRequired)}</td><td>{E(p.ReferenceNumber)}</td><td>{E(p.Notes)}</td></tr>");
        sb.Append("</tbody></table></div>");

        RenderKeyValues(sb, "Interference / Site Works", new Dictionary<string, string?>
        {
            ["Fuel delivery/receipt scheduled at"] = vm.Permit.Interference.FuelDeliveryReceiptScheduledAt,
            ["Other work planned for day"] = YN(vm.Permit.Interference.HasOtherWorkPlannedForDay),
            ["Other work details"] = vm.Permit.Interference.OtherWorkPlannedForDayDetails,
            ["Other work reference"] = vm.Permit.Interference.OtherWorkPlannedForDayReferenceNumber,
            ["Gas cylinders/barrels present"] = YN(vm.Permit.Interference.HasPresenceOfGasCylindersOrBarrels),
            ["Gas cylinders details"] = vm.Permit.Interference.PresenceOfGasCylindersOrBarrelsDetails,
            ["Nearby work planned"] = YN(vm.Permit.Interference.HasOtherNearbyWorkPlanned),
            ["Nearby work details"] = vm.Permit.Interference.OtherNearbyWorkPlannedDetails,
            ["Nearby work reference"] = vm.Permit.Interference.OtherNearbyWorkReferenceNumber
        });

        RenderSelectedChips(sb, "Compulsory Safety Measures", SelectedFlags(new Dictionary<string, bool>
        {
            ["Personnel informed"] = vm.Permit.CompulsorySafetyMeasures.PersonnelInformed,
            ["Phones/cameras switched off"] = vm.Permit.CompulsorySafetyMeasures.MobilePhonesCamerasEtcSwitchedOff,
            ["Closure of station"] = vm.Permit.CompulsorySafetyMeasures.ClosureOfStation,
            ["Hot work restricted in classified areas"] = vm.Permit.CompulsorySafetyMeasures.HotWorkRestrictedInClassifiedAreas,
            ["Distribution stoppage partial"] = vm.Permit.CompulsorySafetyMeasures.DistributionStoppagePartial,
            ["Distribution stoppage total"] = vm.Permit.CompulsorySafetyMeasures.DistributionStoppageTotal,
            ["Protective clothing"] = vm.Permit.CompulsorySafetyMeasures.ProtectiveClothingRequired,
            ["Hearing protection"] = vm.Permit.CompulsorySafetyMeasures.HearingProtectionRequired,
            ["Hard hat"] = vm.Permit.CompulsorySafetyMeasures.HardHatRequired,
            ["Goggles/face shield"] = vm.Permit.CompulsorySafetyMeasures.GogglesOrFaceShieldRequired,
            ["Visibility vest"] = vm.Permit.CompulsorySafetyMeasures.VisibilityVestRequired,
            ["Steel toe shoes"] = vm.Permit.CompulsorySafetyMeasures.SteelToeCapShoesRequired,
            ["High visibility coverall"] = vm.Permit.CompulsorySafetyMeasures.HighVisibilityCoverallRequired,
            ["Dust mask"] = vm.Permit.CompulsorySafetyMeasures.DustMaskRequired,
            ["Gloves"] = vm.Permit.CompulsorySafetyMeasures.GlovesRequired
        }), vm.Permit.CompulsorySafetyMeasures.AdditionalNotes);

        RenderSelectedChips(sb, "Nature of Works", GetWorksSelections(vm.Permit.NatureOfWorks), vm.Permit.NatureOfWorks.OtherWork.Comment);
        RenderSelectedChips(sb, "Nature of Risks", GetRiskSelections(vm.Permit.NatureOfRisks), vm.Permit.NatureOfRisks.OtherRisksNotes);
        RenderSelectedChips(sb, "Prevention Measures", GetPreventionSelections(vm.Permit.PreventionMeasures), vm.Permit.PreventionMeasures.OtherPreventionMeasuresNotes);

        sb.Append("<div class=\"section\"><h2>Derived Required Child Permits</h2><div class=\"chips\">");
        foreach (var d in vm.DerivedPermits)
            sb.Append($"<div class=\"chip\"><strong>{E(d.PermitLabel)}</strong> - {E(d.Reason)}</div>");
        sb.Append("</div></div>");

        sb.Append("<div class=\"section\"><h2>Declaration & Signatures</h2>");
        if (!string.IsNullOrWhiteSpace(vm.Permit.Declaration.DeclarationText))
            sb.Append($"<p>{E(vm.Permit.Declaration.DeclarationText)}</p>");
        sb.Append("<div class=\"sig\">");
        RenderSignature(sb, "Issuing Authority", vm.Permit.Declaration.IssuingAuthority);
        RenderSignature(sb, "Performing Authority", vm.Permit.Declaration.PerformingAuthority);
        RenderSignature(sb, "Site Acknowledgement (Client)", vm.Permit.Declaration.SiteAcknowledgement);
        sb.Append("</div></div>");

        RenderRevalidationTable(sb, vm.Permit.Revalidations);
        RenderHandbackTable(sb, vm.Permit.HandBackEntries);

        RenderKeyValues(sb, "Withdrawal", new Dictionary<string, string?>
        {
            ["Withdrawn"] = YN(vm.Permit.Withdrawal.IsWithdrawn),
            ["Scope changes"] = YN(vm.Permit.Withdrawal.ScopeOfWorkChanges),
            ["Permit rules violation"] = YN(vm.Permit.Withdrawal.PermitRulesViolation),
            ["Accident occurrence"] = YN(vm.Permit.Withdrawal.AccidentOccurrence),
            ["Issuing/performing authority absent"] = YN(vm.Permit.Withdrawal.IssuingOrPerformingAuthorityNotOnSite),
            ["Other reason"] = vm.Permit.Withdrawal.OtherReason,
            ["Notes"] = vm.Permit.Withdrawal.Notes
        });

        if (!string.IsNullOrWhiteSpace(vm.Permit.Notes))
            sb.Append($"<div class=\"section\"><h2>General Notes</h2><p>{E(vm.Permit.Notes)}</p></div>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    public byte[] RenderPdf(WorkAuthorizationMasterPermitDto permit)
    {
        permit.DerivedRequiredPermits = _rules.GetDerivedPermits(permit);
        var selectedWorks = GetWorksSelections(permit.NatureOfWorks);
        var selectedRisks = GetRiskSelections(permit.NatureOfRisks);
        var selectedPrevention = GetPreventionSelections(permit.PreventionMeasures);

        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                PdfTheme.ApplyA4PageDefaults(p);
                p.Header().Column(c =>
                {
                    c.Spacing(5);
                    var logo = PdfTheme.LoadPrimaryLogoBytes(_env);
                    if (logo != null)
                        c.Item().Height(PdfTheme.DocumentLogoHeight).Width(PdfTheme.DocumentLogoWidth).AlignLeft().AlignTop().Image(logo).FitArea();
                    c.Item().Text($"Work Authorisation {permit.WorkAuthorizationNumber}").Bold().FontSize(16);
                    c.Item().Text($"Permit ID: {permit.PermitGuid}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    c.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Lighten1);
                });
                p.Content().Column(c =>
                {
                    c.Spacing(8);
                    c.Item().Text($"Site: {permit.Header.SiteName ?? "—"} | Scope: {permit.Header.ScopeOfWorks ?? "—"}");
                    c.Item().Text($"Contractor: {permit.LocationTask.ContractorCompany ?? "—"} | Task: {permit.LocationTask.TaskDescription ?? "—"}");
                    c.Item().Text("Derived required permits").Bold();
                    foreach (var d in permit.DerivedRequiredPermits)
                        c.Item().Text($"- {d.PermitLabel}: {d.Reason}").FontSize(10);

                    c.Item().Text("Selected nature of works").Bold();
                    foreach (var v in selectedWorks.Take(30))
                        c.Item().Text($"- {v}").FontSize(10);

                    c.Item().Text("Selected nature of risks").Bold();
                    foreach (var v in selectedRisks.Take(30))
                        c.Item().Text($"- {v}").FontSize(10);

                    c.Item().Text("Selected prevention measures").Bold();
                    foreach (var v in selectedPrevention.Take(30))
                        c.Item().Text($"- {v}").FontSize(10);

                    c.Item().PaddingTop(8).Text("Declaration & signatures").Bold();
                    AddSignatureBlock(c, "Issuing authority", permit.Declaration.IssuingAuthority);
                    AddSignatureBlock(c, "Performing authority", permit.Declaration.PerformingAuthority);
                    AddSignatureBlock(c, "Site acknowledgement (client)", permit.Declaration.SiteAcknowledgement);
                });
                PdfTheme.RenderGeneratedFooter(p, "Generated");
            });
        });
        return doc.GeneratePdf();
    }

    private static void AddSignatureBlock(ColumnDescriptor c, string title, WorkAuthorizationSignatureDto sig)
    {
        c.Item().PaddingTop(6).Text(title).SemiBold().FontSize(11);
        var signed = sig.SignedDateTime.HasValue ? sig.SignedDateTime.Value.ToString("u") + " UTC" : "—";
        c.Item().Text($"{sig.Name ?? "—"} | {sig.Role ?? "—"} | Signed: {signed}").FontSize(9);
        var img = TryDecodeSignatureImage(sig.SignatureImageBase64);
        if (img != null)
            c.Item().PaddingTop(4).MaxHeight(100).Image(img).FitWidth();
    }

    private static byte[]? TryDecodeSignatureImage(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        var s = base64.Trim();
        var comma = s.IndexOf(',');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            s = s[(comma + 1)..];
        try
        {
            return Convert.FromBase64String(s);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetWorksSelections(WorkAuthorizationNatureOfWorksSectionDto s)
    {
        var selected = new List<string>();
        selected.AddRange(s.MovementCirculationOptions.Where(x => x.IsSelected).Select(x => x.Label));
        selected.AddRange(s.HotWorkOptions.Where(x => x.IsSelected).Select(x => x.Label));
        selected.AddRange(s.LiftingOptions.Where(x => x.IsSelected).Select(x => x.Label));
        selected.AddRange(s.WorkAtHeightsOptions.Where(x => x.IsSelected).Select(x => x.Label));
        if (s.WorkInExplosionRiskZone.IsSelected) selected.Add(s.WorkInExplosionRiskZone.Label);
        if (s.EquipmentTools.IsSelected) selected.Add(s.EquipmentTools.Label);
        if (s.DangerousMachineryWork.IsSelected) selected.Add(s.DangerousMachineryWork.Label);
        if (s.HazardousChemicalProducts.IsSelected) selected.Add(s.HazardousChemicalProducts.Label);
        if (s.ExtremeTemperatureWork.IsSelected) selected.Add(s.ExtremeTemperatureWork.Label);
        if (s.PressurisedEquipmentUse.IsSelected) selected.Add(s.PressurisedEquipmentUse.Label);
        if (s.ManualHandling.IsSelected) selected.Add($"{s.ManualHandling.Label} {s.ManualHandlingTypesAndWeight}".Trim());
        if (s.ExcavationWork.IsSelected) selected.Add(s.ExcavationWork.Label);
        if (s.ElectricalWork.IsSelected) selected.Add($"{s.ElectricalWork.Label} (LV:{YN(s.ElectricalWorkLv)}, HV:{YN(s.ElectricalWorkHv)})");
        if (s.ConfinedAtmosphereWork.IsSelected) selected.Add(s.ConfinedAtmosphereWork.Label);
        if (s.DrainingRinsingWork.IsSelected) selected.Add(s.DrainingRinsingWork.Label);
        if (s.CleaningDegassingWork.IsSelected) selected.Add(s.CleaningDegassingWork.Label);
        if (s.RadiographicTestingWork.IsSelected) selected.Add(s.RadiographicTestingWork.Label);
        if (s.NoisyWork.IsSelected) selected.Add(s.NoisyWork.Label);
        if (s.OtherWork.IsSelected) selected.Add($"{s.OtherWork.Label} {s.OtherWork.Comment}".Trim());
        return selected.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
    }

    private static List<string> GetRiskSelections(WorkAuthorizationNatureOfRisksSectionDto s)
    {
        var all = new List<ToggleOptionDto>();
        all.AddRange(s.TrafficAndMovementRisks);
        all.AddRange(s.FireExplosionRisks);
        all.AddRange(s.MechanicalRisks);
        all.AddRange(s.ChemicalThermalRisks);
        all.AddRange(s.ManualHandlingRisks);
        all.AddRange(s.ExcavationRisks);
        all.AddRange(s.ElectricalRisks);
        all.AddRange(s.ConfinedSpaceRisks);
        all.AddRange(s.OverheadAndDroppedObjectRisks);
        all.AddRange(s.HeightRisks);
        all.AddRange(s.RadiographicRisks);
        all.AddRange(s.NoiseRisks);
        return all.Where(x => x.IsSelected).Select(x => x.Label).Distinct().ToList();
    }

    private static List<string> GetPreventionSelections(WorkAuthorizationPreventionMeasuresSectionDto s)
    {
        var selected = new List<string>();
        if (s.ActivitiesHaltedCompletely) selected.Add("Activities halted completely");
        if (s.ActivitiesHaltedPartially) selected.Add($"Activities halted partially {s.ActivitiesHaltedSpecify}".Trim());

        var all = new List<ToggleOptionDto>();
        all.AddRange(s.TrafficAndOperationalControls);
        all.AddRange(s.ExplosionAndAtexControls);
        all.AddRange(s.MechanicalControls);
        all.AddRange(s.ChemicalAndPpeControls);
        all.AddRange(s.PressureAndManualHandlingControls);
        all.AddRange(s.ExcavationControls);
        all.AddRange(s.ElectricalControls);
        all.AddRange(s.ConfinedSpaceControls);
        all.AddRange(s.LiftingControls);
        all.AddRange(s.WorkingAtHeightsControls);
        all.AddRange(s.CleaningDegassingControls);
        all.AddRange(s.RadiographicControls);
        all.AddRange(s.NoiseControls);
        selected.AddRange(all.Where(x => x.IsSelected).Select(x => x.Label));
        return selected.Distinct().ToList();
    }

    private static List<string> SelectedFlags(Dictionary<string, bool> flags)
        => flags.Where(kv => kv.Value).Select(kv => kv.Key).ToList();

    private static void RenderKeyValues(StringBuilder sb, string title, Dictionary<string, string?> values)
    {
        sb.Append($"<div class=\"section\"><h2>{E(title)}</h2><div class=\"grid\">");
        foreach (var kv in values)
            sb.Append($"<div class=\"kv\"><div class=\"k\">{E(kv.Key)}</div><div class=\"v\">{E(kv.Value)}</div></div>");
        sb.Append("</div></div>");
    }

    private static void RenderSelectedChips(StringBuilder sb, string title, List<string> selected, string? notes = null)
    {
        sb.Append($"<div class=\"section\"><h2>{E(title)}</h2>");
        if (selected.Count == 0) sb.Append("<div class=\"muted\">No selections captured.</div>");
        else
        {
            sb.Append("<div class=\"chips\">");
            foreach (var s in selected) sb.Append($"<div class=\"chip\">{E(s)}</div>");
            sb.Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(notes)) sb.Append($"<p><strong>Notes:</strong> {E(notes)}</p>");
        sb.Append("</div>");
    }

    private static void RenderSignature(StringBuilder sb, string title, WorkAuthorizationSignatureDto s)
    {
        sb.Append($"<div class=\"sigbox\"><div class=\"k\">{E(title)}</div><div class=\"v\">{E(s.Name)} ({E(s.Role)})</div><div class=\"muted\">{FDateTime(s.SignedDateTime)}</div>");
        var img = !string.IsNullOrWhiteSpace(s.SignatureImageBase64)
            ? $"data:image/png;base64,{s.SignatureImageBase64}"
            : s.SignatureImageUrl;
        if (!string.IsNullOrWhiteSpace(img))
            sb.Append($"<img class=\"sigimg\" src=\"{E(img)}\" alt=\"signature\" />");
        else
            sb.Append("<div class=\"muted\">Not signed</div>");
        sb.Append("</div>");
    }

    private static void RenderRevalidationTable(StringBuilder sb, List<WorkAuthorizationRevalidationEntryDto> rows)
    {
        sb.Append("<div class=\"section\"><h2>Revalidations</h2><table><thead><tr><th>Date</th><th>Time From</th><th>Time To</th><th>Issuing Authority</th><th>Performing Authority</th></tr></thead><tbody>");
        if (rows.Count == 0) sb.Append("<tr><td colspan=\"5\">No revalidations.</td></tr>");
        foreach (var r in rows)
            sb.Append($"<tr><td>{FDate(r.Date)}</td><td>{FTime(r.TimeFrom)}</td><td>{FTime(r.TimeTo)}</td><td>{E(r.IssuingAuthorityName)}</td><td>{E(r.PerformingAuthorityName)}</td></tr>");
        sb.Append("</tbody></table></div>");
    }

    private static void RenderHandbackTable(StringBuilder sb, List<WorkAuthorizationHandbackEntryDto> rows)
    {
        sb.Append("<div class=\"section\"><h2>Hand Over / Hand Back</h2><table><thead><tr><th>Date</th><th>Works Completed</th><th>Issuing Authority</th><th>Performing Authority</th></tr></thead><tbody>");
        if (rows.Count == 0) sb.Append("<tr><td colspan=\"4\">No handback entries.</td></tr>");
        foreach (var r in rows)
            sb.Append($"<tr><td>{FDate(r.Date)}</td><td>{YN(r.WorksCompleted == true)}</td><td>{E(r.IssuingAuthorityName)}</td><td>{E(r.PerformingAuthorityName)}</td></tr>");
        sb.Append("</tbody></table></div>");
    }

    private static string E(string? s) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s) ? "—" : s);
    private static string YN(bool b) => b ? "Yes" : "No";
    private static string FDate(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";
    private static string FDateTime(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    private static string FTime(TimeSpan? t) => t?.ToString(@"hh\:mm") ?? "—";
}
