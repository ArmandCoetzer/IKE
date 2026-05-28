using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Models;

namespace Ike.Api.Services;

public class ChildPermitDocumentationPdfRenderer : IChildPermitDocumentationPdfRenderer
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ChildPermitDocumentationPdfRenderer(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<byte[]?> RenderAsync(Guid jobPermitId, CancellationToken ct = default)
    {
        var permit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == jobPermitId, ct);
        if (permit == null || permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
            return null;

        var job = permit.JobCard;
        var site = job?.Site;
        var template = permit.PermitTemplate;
        var typeName = PermitTemplateDurationHelper.PrimaryDisplayName(template) ?? "Work permit";

        var checklistLines = BuildChecklistLines(template?.ChecklistJson, permit.ChecklistSnapshotJson);
        var formSchema = PermitFormJsonHelper.ParseSchema(template?.FormSchemaJson);
        var formVals = PermitFormJsonHelper.ParseValues(permit.FormSnapshotJson);

        var logoBytes = PdfTheme.LoadPrimaryLogoBytes(_env);
        var (sigBytes, sigLabel) = LoadSignatureImage(permit);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                PdfTheme.ApplyA4PageDefaults(page);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(head =>
                {
                    head.Spacing(5);
                    head.Item().Row(row =>
                    {
                        PdfTheme.RenderLogoCell(row, logoBytes);
                        row.RelativeItem().AlignRight().Column(col =>
                        {
                            col.Item().Text("Work permit record").Bold().FontSize(12);
                            col.Item().Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    head.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().Column(c =>
                {
                    c.Spacing(10);
                    c.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.White).Padding(12).Column(box =>
                    {
                        box.Item().Text(typeName).Bold().FontSize(15);
                        box.Item().Text($"Job card: {job?.JobCardNumber ?? "—"}  •  Status: {permit.Status ?? "—"}");
                        if (site != null)
                            box.Item().Text($"Site: {site.Name ?? "—"}");
                        if (site?.Company != null)
                            box.Item().Text($"Client: {site.Company.Name ?? "—"}");
                        if (permit.RequestedAt != default)
                            box.Item().Text($"Requested: {permit.RequestedAt:yyyy-MM-dd HH:mm} UTC");
                        if (permit.ApprovedAt.HasValue)
                            box.Item().Text($"Approved / active from: {permit.ApprovedAt:yyyy-MM-dd HH:mm} UTC");
                        if (permit.ValidFrom.HasValue)
                            box.Item().Text($"Valid from: {permit.ValidFrom:yyyy-MM-dd HH:mm} UTC");
                        if (permit.ValidTo.HasValue)
                            box.Item().Text($"Valid to: {permit.ValidTo:yyyy-MM-dd HH:mm} UTC");
                    });

                    if (checklistLines.Count > 0)
                    {
                        c.Item().Text("Safety commitments / checklist").Bold().FontSize(12);
                        foreach (var line in checklistLines)
                        {
                            var mark = line.Checked ? "☑" : "☐";
                            c.Item().Text($"{mark} {line.Label}");
                        }
                    }

                    if (formSchema.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Permit form").Bold().FontSize(12);
                        foreach (var f in formSchema)
                        {
                            formVals.TryGetValue(f.Id, out var val);
                            c.Item().Text($"{f.Label ?? f.Id}: {val ?? "—"}").FontSize(9);
                        }
                    }
                    else if (formVals.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Form values").Bold().FontSize(12);
                        foreach (var kv in formVals)
                            c.Item().Text($"{kv.Key}: {kv.Value}").FontSize(9);
                    }

                    c.Item().PaddingTop(10).Text("Files on record").Bold().FontSize(11);
                    foreach (var a in permit.Attachments.OrderBy(x => x.UploadedAt))
                        c.Item().Text($"• {a.FileName} ({a.UploadedAt:yyyy-MM-dd HH:mm} UTC)").FontSize(8).FontColor(Colors.Grey.Darken2);

                    if (sigBytes != null)
                    {
                        c.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        c.Item().PaddingTop(8).Text("Client / site signature").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken2);
                        if (!string.IsNullOrWhiteSpace(sigLabel))
                            c.Item().Text(sigLabel).FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                        c.Item().PaddingTop(6).Width(120).Height(40).Image(sigBytes).FitArea();
                    }
                });

                PdfTheme.RenderGeneratedFooter(page, "Generated from the live permit record.");
            });
        });

        // GeneratePdf is CPU-bound and does not observe CancellationToken; linking the request token
        // causes TaskCanceledException when the client disconnects or the pipeline aborts mid-call.
        return await Task.Run(() => doc.GeneratePdf(), CancellationToken.None);
    }

    private static int ClientSignatureFileScore(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return 0;
        var f = fileName.ToLowerInvariant();
        if (f.Contains("signature")) return 100;
        if (f.Contains("sign-off") || f.Contains("signoff")) return 95;
        if (f.Contains("client") && (f.Contains("sign") || f.Contains("sig"))) return 90;
        if (f.Contains("site") && (f.Contains("sign") || f.Contains("sig"))) return 88;
        if (f.Contains("signed")) return 70;
        if (f.Contains("client")) return 45;
        if (f.Contains("sign") || f.Contains("sig")) return 35;
        return 0;
    }

    private (byte[]? bytes, string label) LoadSignatureImage(JobPermit permit)
    {
        var candidates = new List<(JobPermitAttachment att, byte[] bytes)>();
        foreach (var att in permit.Attachments.OrderBy(a => a.UploadedAt))
        {
            var ext = Path.GetExtension(att.FileName ?? "").ToLowerInvariant();
            if (ext is not ".png" and not ".jpg" and not ".jpeg") continue;
            var rel = FilePathHelper.ValidateAndNormalize(att.FilePath);
            if (rel == null) continue;
            var full = Path.Combine(_env.ContentRootPath, rel);
            if (!File.Exists(full)) continue;
            try
            {
                var b = File.ReadAllBytes(full);
                if (b.Length > 0) candidates.Add((att, b));
            }
            catch
            {
                // ignore
            }
        }

        if (candidates.Count == 0) return (null, "");

        var ranked = candidates
            .Select(c => (c, score: ClientSignatureFileScore(c.att.FileName)))
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.c.att.UploadedAt)
            .ToList();

        var best = ranked[0];
        if (best.score >= 35)
            return (best.c.bytes, best.c.att.FileName ?? "Signature");

        if (candidates.Count == 1)
            return (candidates[0].bytes, candidates[0].att.FileName ?? "");

        return (null, "");
    }

    private static List<(string Label, bool Checked)> BuildChecklistLines(string? templateJson, string? snapshotJson)
    {
        var template = ParseChecklistTemplatePairs(templateJson);
        var snapshot = ParseChecklistSnapshotPairs(snapshotJson);
        var list = new List<(string Label, bool Checked)>();
        if (template.Count > 0)
        {
            foreach (var t in template)
            {
                var snap = snapshot.FirstOrDefault(s => string.Equals(s.Id, t.Id, StringComparison.OrdinalIgnoreCase));
                list.Add((string.IsNullOrWhiteSpace(t.Label) ? t.Id : t.Label, snap.Checked));
            }
        }
        else
        {
            foreach (var s in snapshot)
                list.Add((string.IsNullOrWhiteSpace(s.Label) ? s.Id : s.Label, s.Checked));
        }

        return list;
    }

    private static List<(string Id, string Label)> ParseChecklistTemplatePairs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<(string, string)>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
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

    private static List<(string Id, string Label, bool Checked)> ParseChecklistSnapshotPairs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<(string, string, bool)>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var list = new List<(string, string, bool)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var label = e.TryGetProperty("label", out var lbProp) ? lbProp.GetString() ?? "" : "";
                var chk = e.TryGetProperty("checked", out var chkProp) && chkProp.GetBoolean();
                list.Add((id, label, chk));
            }

            return list;
        }
        catch
        {
            return new List<(string, string, bool)>();
        }
    }
}
