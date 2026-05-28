using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Models;

namespace Ike.Api.Services;

public class EmailService : IEmailService
{
    /// <summary>Last-resort dev inbox if <c>DebugSettings:DevelopmentEmail</c> and <c>DefaultDevelopmentEmail</c> are both unset.</summary>
    private const string HardcodedFallbackDevelopmentInbox = "armand.coetzer0108@gmail.com";

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDocumentService _documentService;
    private readonly IWebHostEnvironment _env;
    private readonly IChildPermitDocumentationPdfRenderer _childPermitPdf;
    private readonly IEmailProvider _emailProvider;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        ApplicationDbContext db,
        IConfiguration config,
        IDocumentService documentService,
        IWebHostEnvironment env,
        IChildPermitDocumentationPdfRenderer childPermitPdf,
        IEmailProvider emailProvider,
        ILogger<EmailService> logger)
    {
        _db = db;
        _config = config;
        _documentService = documentService;
        _env = env;
        _childPermitPdf = childPermitPdf;
        _emailProvider = emailProvider;
        _logger = logger;
    }

    /// <summary>
    /// All outbound email in this service must flow through <see cref="SendAsync"/> or <see cref="SendWithPartsAsync"/> so this runs once per message.
    /// When <c>DebugSettings:IsDevelopmentMode</c> is <c>true</c>, mail is never delivered to <paramref name="intendedTo"/>; it goes to
    /// <c>DebugSettings:DevelopmentEmail</c>, then <c>DebugSettings:DefaultDevelopmentEmail</c>, then <see cref="HardcodedFallbackDevelopmentInbox"/>.
    /// When dev mode is <c>false</c>, <paramref name="intendedTo"/> is used as-is.
    /// </summary>
    private string ResolveRecipient(string intendedTo)
    {
        if (!_config.GetValue<bool>("DebugSettings:IsDevelopmentMode"))
            return intendedTo;

        var configured = _config["DebugSettings:DevelopmentEmail"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!string.Equals(intendedTo, configured, StringComparison.OrdinalIgnoreCase))
                _logger.LogInformation("Email dev routing: intended {Intended} -> {Resolved}", intendedTo, configured);
            return configured;
        }

        var fromConfig = _config["DebugSettings:DefaultDevelopmentEmail"]?.Trim();
        var resolved = !string.IsNullOrWhiteSpace(fromConfig) ? fromConfig : HardcodedFallbackDevelopmentInbox;
        if (!string.Equals(intendedTo, resolved, StringComparison.OrdinalIgnoreCase))
            _logger.LogInformation(
                "Email dev routing: intended {Intended} -> {Resolved} (DebugSettings:DevelopmentEmail empty; using default dev inbox)",
                intendedTo,
                resolved);
        return resolved;
    }

    /// <summary>Single canonical closing for every outbound email (matches supplier draft style).</summary>
    private const string ProfessionalSignOff = "Kind regards,\nIan Kleyn Electrical";

    /// <summary>
    /// Strips trailing sign-off blocks so we never double up: supplier drafts and other templates often already end with
    /// <c>Kind regards,</c> plus a company line, while this service also appends the same closing.
    /// </summary>
    private static string StripTrailingKnownSignOffs(string normalizedBody)
    {
        var t = normalizedBody.TrimEnd();
        var re = new Regex(
            @"(?:\n)*\s*Kind regards\s*,?\s*\n+\s*(?:Ian Kleyn Electrical|IKE)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        while (true)
        {
            var next = re.Replace(t, "").TrimEnd();
            if (next == t) return t;
            t = next;
        }
    }

    private static string EnsureProfessionalSignOff(string? body)
    {
        var cleaned = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        cleaned = StripTrailingKnownSignOffs(cleaned);
        if (string.IsNullOrWhiteSpace(cleaned))
            return ProfessionalSignOff;
        return cleaned + "\n\n" + ProfessionalSignOff;
    }

    private static string BuildBrandedHtmlBody(string subject, string plainBody, string? logoContentId)
    {
        var encodedSubject = WebUtility.HtmlEncode(subject ?? "IKE notification");
        var bodyHtml = ConvertPlainTextToHtml(plainBody);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(encodedSubject).Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif;color:#111827;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#f3f4f6;padding:24px 12px;\">");
        sb.Append("<tr><td align=\"center\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:680px;background:#ffffff;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden;\">");
        sb.Append("<tr><td style=\"padding:0px 15px;border-bottom:4px solid #e31837;background:#ffffff;\">");
        if (!string.IsNullOrWhiteSpace(logoContentId))
        {
            sb.Append("<img src=\"cid:").Append(logoContentId).Append("\" alt=\"Ian Kleyn Electrical\" style=\"max-height:130px;width:auto;display:block;\">");
        }
        else
        {
            sb.Append("<div style=\"font-weight:700;font-size:18px;color:#111827;\">Ian Kleyn Electrical</div>");
        }
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:22px 20px 16px 20px;\">");
        sb.Append("<div style=\"font-size:22px;line-height:1.25;font-weight:700;color:#111827;margin:0 0 14px 0;\">").Append(encodedSubject).Append("</div>");
        sb.Append("<div style=\"font-size:15px;line-height:1.6;color:#1f2937;\">").Append(bodyHtml).Append("</div>");
        sb.Append("</td></tr>");
        sb.Append("</table>");
        sb.Append("</td></tr></table></body></html>");

        return sb.ToString();
    }

    private static string ConvertPlainTextToHtml(string plain)
    {
        var safe = WebUtility.HtmlEncode(plain ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            return "<p style=\"margin:0;\">&nbsp;</p>";

        var blocks = safe
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.None)
            .Select(b => b.Replace("\n", "<br/>", StringComparison.Ordinal))
            .Select(b => $"<p style=\"margin:0 0 12px 0;\">{b}</p>");
        return string.Join(string.Empty, blocks);
    }

    public async Task<bool> SendQuoteToClientAsync(Guid quoteId, string? toEmail = null, bool attachPdf = false, CancellationToken ct = default)
    {
        var quote = await _db.Quotes.AsNoTracking().Include(q => q.Company).FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote == null)
            return false;
        var to = toEmail ?? quote.Company?.ContactEmail;
        if (string.IsNullOrEmpty(to))
            return false;
        byte[]? attachmentBytes = null;
        string? attachmentName = null;
        if (attachPdf)
        {
            if (quote.IsUploaded)
            {
                var uploaded = await LoadUploadedQuoteAttachmentAsync(quote, ct);
                if (uploaded == null)
                    return false;
                attachmentBytes = uploaded.Value.bytes;
                attachmentName = uploaded.Value.fileName;
            }
            else
            {
                attachmentBytes = await _documentService.GetQuotePdfAsync(quoteId, ct);
                attachmentName = $"Quote-{quote.QuoteNumber}.pdf";
            }
        }
        return await SendAsync(
            to,
            $"Quote {quote.QuoteNumber}",
            "Please find your quote attached for your review.",
            attachmentBytes,
            attachmentName,
            ct);
    }

    private async Task<(byte[] bytes, string fileName)?> LoadUploadedQuoteAttachmentAsync(Quote quote, CancellationToken ct)
    {
        var safePath = FilePathHelper.ValidateAndNormalize(quote.UploadedFilePath);
        if (safePath == null)
        {
            _logger.LogWarning("Uploaded quote email not sent: stored file path is missing or invalid for quote {QuoteId}.", quote.Id);
            return null;
        }

        var fullPath = Path.Combine(_env.ContentRootPath, safePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Uploaded quote email not sent: uploaded file missing for quote {QuoteId}. RelativePath={RelativePath}", quote.Id, safePath);
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, ct);
            if (bytes.Length == 0)
            {
                _logger.LogWarning("Uploaded quote email not sent: uploaded file is empty for quote {QuoteId}. RelativePath={RelativePath}", quote.Id, safePath);
                return null;
            }

            var fileName = string.IsNullOrWhiteSpace(quote.UploadedFileName)
                ? Path.GetFileName(safePath)
                : quote.UploadedFileName.Trim();
            return (bytes, fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Uploaded quote email not sent: uploaded file could not be read for quote {QuoteId}. RelativePath={RelativePath}", quote.Id, safePath);
            return null;
        }
    }

    public async Task<bool> SendInvoiceToClientAsync(Guid invoiceId, string? toEmail = null, bool attachPdf = false, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking().Include(i => i.Company).FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null)
            return false;
        var to = toEmail ?? invoice.Company?.ContactEmail;
        if (string.IsNullOrEmpty(to))
            return false;
        byte[]? attachmentBytes = null;
        string? attachmentName = null;
        if (attachPdf)
        {
            attachmentBytes = await _documentService.GetInvoicePdfAsync(invoiceId, ct);
            attachmentName = $"Invoice-{invoice.InvoiceNumber}.pdf";
        }
        return await SendAsync(
            to,
            $"Invoice {invoice.InvoiceNumber}",
            "Please find your invoice attached for your records.",
            attachmentBytes,
            attachmentName,
            ct);
    }

    public async Task SendPaymentReminderAsync(Guid invoiceId, string? toEmail = null, bool attachPdf = false, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking().Include(i => i.Company).FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null || InvoiceStatus.IsPaid(invoice.Status))
            return;
        var to = toEmail ?? invoice.Company?.ContactEmail;
        if (string.IsNullOrEmpty(to))
            return;
        byte[]? attachmentBytes = null;
        string? attachmentName = null;
        if (attachPdf)
        {
            attachmentBytes = await _documentService.GetInvoicePdfAsync(invoiceId, ct);
            attachmentName = $"Invoice-{invoice.InvoiceNumber}.pdf";
        }
        var body = "This is a friendly reminder that the invoice below remains outstanding. Please arrange payment at your earliest convenience.";
        await SendAsync(to, $"Payment reminder: Invoice {invoice.InvoiceNumber}", body, attachmentBytes, attachmentName, ct);
    }

    public async Task<bool> SendPermitDocumentationPackageToClientAsync(Guid jobPermitId, byte[]? primaryPdf, string? primaryPdfFileName, string? toEmail = null, CancellationToken ct = default)
    {
        var permit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == jobPermitId, ct);
        if (permit?.JobCard?.Site?.Company == null)
        {
            _logger.LogWarning("Permit email not sent: permit {PermitId} or related job/site/company was not found.", jobPermitId);
            return false;
        }
        var to = toEmail ?? permit.JobCard.Site.Company.ContactEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Permit email not sent: no client email is set for permit {PermitId}.", jobPermitId);
            return false;
        }
        var permitTypeName = permit.PermitTemplate?.PermitType?.Name ?? "Permit";
        var jobNumber = permit.JobCard.JobCardNumber;
        var siteName = permit.JobCard.Site.Name ?? "the site";
        var companyName = permit.JobCard.Site.Company.Name ?? "your organisation";
        var subject = $"Permit documentation — Job Card {jobNumber} ({permitTypeName})";
        var body = $"Dear {companyName},\n\n" +
            $"Please find attached the permit documentation for Job Card {jobNumber} at {siteName}.\n\n" +
            $"Permit type: {permitTypeName}.";

        var effectivePdf = primaryPdf;
        var effectivePdfName = primaryPdfFileName;
        if (effectivePdf is not { Length: > 0 } && permit.PermitTemplate?.PermitType?.IsWorkAuthorisation != true)
        {
            var rendered = await _childPermitPdf.RenderAsync(jobPermitId, ct);
            if (rendered is { Length: > 0 })
            {
                effectivePdf = rendered;
                var safeBase = string.Join("_", (permitTypeName ?? "permit").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                effectivePdfName = $"{(string.IsNullOrWhiteSpace(safeBase) ? "permit" : safeBase)}-documentation.pdf";
            }
        }

        var parts = new List<(byte[] bytes, string fileName)>();
        if (effectivePdf is { Length: > 0 } && !string.IsNullOrWhiteSpace(effectivePdfName))
            parts.Add((effectivePdf, effectivePdfName));
        var skipLooseSignatures = effectivePdf is { Length: > 0 };
        foreach (var att in permit.Attachments.OrderBy(a => a.UploadedAt))
        {
            if (skipLooseSignatures && (att.FileName ?? "").Contains("signature", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = FilePathHelper.ValidateAndNormalize(att.FilePath);
            if (rel == null) continue;
            var fullPath = Path.Combine(_env.ContentRootPath, rel);
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Permit email attachment skipped: file missing for permit {PermitId}. RelativePath={RelativePath}", jobPermitId, rel);
                continue;
            }
            try
            {
                var bytes = await File.ReadAllBytesAsync(fullPath, ct);
                if (bytes.Length == 0) continue;
                var name = string.IsNullOrWhiteSpace(att.FileName) ? Path.GetFileName(rel) : att.FileName;
                parts.Add((bytes, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Permit email attachment skipped: file could not be read for permit {PermitId}. RelativePath={RelativePath}", jobPermitId, rel);
            }
        }

        if (parts.Count == 0)
        {
            _logger.LogWarning("Permit email not sent: no permit documents were available for permit {PermitId}.", jobPermitId);
            return false;
        }

        return await SendWithPartsAsync(to, subject, body, parts, ct);
    }

    public async Task SendClientInviteEmailAsync(string toEmail, string inviteLink, string? companyName = null, CancellationToken ct = default)
    {
        var intro = companyName != null
            ? $"You have been invited to join {companyName} on our platform."
            : "You have been invited to join our platform.";
        var body = $"{intro}\n\nPlease complete your registration by clicking the link below. This link is valid for 7 days.\n\n{inviteLink}\n\nIf you did not expect this email, you can ignore it.";
        await SendAsync(toEmail, "Complete your registration", body, null, null, ct);
    }

    public async Task SendEmployeeInviteEmailAsync(string toEmail, string inviteLink, string? fullName = null, CancellationToken ct = default)
    {
        var intro = fullName != null
            ? $"Dear {fullName}, you have been invited to join as a team member."
            : "You have been invited to join as a team member.";
        var body = $"{intro}\n\nPlease set your password by clicking the link below. This link is valid for 7 days.\n\n{inviteLink}\n\nIf you did not expect this email, you can ignore it.";
        await SendAsync(toEmail, "Set your password and complete registration", body, null, null, ct);
    }

    public async Task SendJobCardPdfToClientAsync(Guid jobCardId, string? toEmail = null, CancellationToken ct = default)
    {
        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job?.Site?.Company == null)
            return;
        var to = toEmail ?? job.Site.Company.ContactEmail;
        if (string.IsNullOrWhiteSpace(to))
            return;
        var bytes = await _documentService.GetJobCardPdfAsync(jobCardId, ct);
        if (bytes == null || bytes.Length == 0)
            return;
        var num = string.IsNullOrWhiteSpace(job.JobCardNumber) ? jobCardId.ToString() : job.JobCardNumber;
        var companyName = job.Site.Company.Name ?? "your organisation";
        var siteName = job.Site.Name ?? "the site";
        var subject = $"Job card {num}";
        var body = $"Dear {companyName},\n\nPlease find attached the job card summary for work at {siteName} (Job card {num}).";
        var safeFile = $"JobCard-{num}".Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        foreach (var c in Path.GetInvalidFileNameChars())
            safeFile = safeFile.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(safeFile)) safeFile = "JobCard";
        await SendAsync(to, subject, body, bytes, $"{safeFile}.pdf", ct);
    }

    public async Task SendSupplierStockRequestAsync(Guid supplierQuoteRequestId, string? toEmail = null, CancellationToken ct = default)
    {
        var req = await _db.SupplierQuoteRequests.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Part).ThenInclude(p => p!.Company)
            .Include(r => r.JobCard).ThenInclude(j => j!.Site)
            .FirstOrDefaultAsync(r => r.Id == supplierQuoteRequestId, ct);
        if (req?.Supplier == null || req.Part == null)
            return;
        var to = toEmail ?? req.Supplier.Email;
        if (string.IsNullOrWhiteSpace(to))
            return;
        var supplierName = req.Supplier.Name;
        var greetingName = string.IsNullOrWhiteSpace(req.Supplier.ContactPerson) ? supplierName : req.Supplier.ContactPerson.Trim();
        var partName = req.Part.Name;
        var partUnit = string.IsNullOrWhiteSpace(req.Part.Unit)
            ? "unit/s"
            : (req.Part.Unit.Trim().Equals("Each", StringComparison.OrdinalIgnoreCase) ? "unit/s" : req.Part.Unit.Trim());
        var qtyText = req.RequestedQuantity.HasValue && req.RequestedQuantity.Value > 0
            ? req.RequestedQuantity.Value.ToString()
            : "unspecified";

        var subject = $"Stock request: {partName}";
        var body =
            $"Dear {greetingName},\n\n" +
            "I hope you are well.\n\n" +
            "Could you please provide pricing and availability for the following stock item:\n\n" +
            $"- {partName}: required amount {qtyText} ({partUnit})\n" +
            (!string.IsNullOrWhiteSpace(req.Notes) ? $"- Notes: {req.Notes}\n" : "") +
            "\nPlease reply with pricing and availability.";

        await SendAsync(to, subject, body, null, null, ct);
    }

    public async Task SendCustomEmailAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
            return;
        await SendAsync(toEmail, subject.Trim(), body, null, null, ct);
    }

    public async Task SendOperationalRiskSummaryAsync(string toEmail, string subject, IReadOnlyList<string> lines, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || lines.Count == 0)
            return;

        var body = "Please find the operational risk summary below:\n\n" + string.Join("\n", lines.Select(l => "- " + l));
        await SendAsync(toEmail, subject, body, null, null, ct);
    }

    private async Task<bool> SendAsync(string to, string subject, string body, byte[]? attachmentBytes = null, string? attachmentName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            return false;

        var intendedTo = to.Trim();
        to = ResolveRecipient(intendedTo);
        if (string.IsNullOrWhiteSpace(to))
            return false;

        var attachments = new List<EmailAttachment>();
        if (attachmentBytes != null && attachmentBytes.Length > 0 && !string.IsNullOrWhiteSpace(attachmentName))
            attachments.Add(new EmailAttachment(attachmentBytes, attachmentName));

        var email = BuildEmailMessage(to, intendedTo, subject, body, attachments);
        _logger.LogInformation("Dispatching email via {Provider}: subject={Subject}, to={To}", _emailProvider.ProviderName, subject, to);
        return await _emailProvider.SendAsync(email, ct);
    }

    private static string SafeZipEntryName(string fileName)
    {
        var n = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(n)) return "file.bin";
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return n.Length > 100 ? n[..100] : n;
    }

    private async Task<bool> SendWithPartsAsync(string to, string subject, string body, IReadOnlyList<(byte[] bytes, string fileName)> parts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to))
            return false;

        var intendedTo = to.Trim();
        to = ResolveRecipient(intendedTo);
        if (string.IsNullOrWhiteSpace(to))
            return false;

        var partsList = parts.Where(p => p.bytes.Length > 0 && !string.IsNullOrWhiteSpace(p.fileName)).ToList();
        const int zipThresholdBytes = 14 * 1024 * 1024;
        var totalBytes = partsList.Sum(p => p.bytes.Length);
        if (totalBytes > zipThresholdBytes && partsList.Count > 1)
        {
            await using var zipMs = new MemoryStream();
            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, true))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (bytes, fileName) in partsList)
                {
                    var baseName = SafeZipEntryName(fileName);
                    var entryName = baseName;
                    var n = 1;
                    while (!used.Add(entryName))
                    {
                        var stem = Path.GetFileNameWithoutExtension(baseName);
                        var ext = Path.GetExtension(baseName);
                        entryName = $"{stem}_{n++}{ext}";
                    }

                    var entry = zip.CreateEntry(entryName);
                    await using var es = entry.Open();
                    await es.WriteAsync(bytes, ct);
                }
            }

            partsList = new List<(byte[], string)> { (zipMs.ToArray(), "permit-documentation-package.zip") };
            body += "\n\nAttachments are bundled in the ZIP file to reduce email size.";
        }

        var attachments = partsList
            .Select(p => new EmailAttachment(
                p.bytes,
                p.fileName,
                p.fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "application/octet-stream"))
            .ToList();

        var email = BuildEmailMessage(to, intendedTo, subject, body, attachments);
        _logger.LogInformation("Dispatching email via {Provider}: subject={Subject}, to={To}, attachmentCount={AttachmentCount}", _emailProvider.ProviderName, subject, to, attachments.Count);
        return await _emailProvider.SendAsync(email, ct);
    }

    private EmailMessage BuildEmailMessage(string to, string intendedTo, string subject, string body, IReadOnlyList<EmailAttachment> attachments)
    {
        var finalBody = EnsureProfessionalSignOff(body);
        var logoBytes = PdfTheme.LoadPrimaryLogoBytes(_env);
        var logoContentId = logoBytes is { Length: > 0 } ? "ike-logo" : null;
        return new EmailMessage
        {
            To = to,
            IntendedTo = intendedTo,
            Subject = subject,
            PlainTextBody = finalBody,
            HtmlBody = BuildBrandedHtmlBody(subject, finalBody, logoContentId),
            Attachments = attachments,
            InlineLogoBytes = logoBytes,
            InlineLogoContentId = logoContentId
        };
    }
}
