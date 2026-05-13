using System.IO.Compression;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tradion.Api.Data;
using Tradion.Api.Helpers;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

public class EmailService : IEmailService
{
    /// <summary>Last-resort dev inbox if <c>DebugSettings:DevelopmentEmail</c> and <c>DefaultDevelopmentEmail</c> are both unset.</summary>
    private const string HardcodedFallbackDevelopmentInbox = "armand.coetzer0108@gmail.com";

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDocumentService _documentService;
    private readonly IWebHostEnvironment _env;
    private readonly IChildPermitDocumentationPdfRenderer _childPermitPdf;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        ApplicationDbContext db,
        IConfiguration config,
        IDocumentService documentService,
        IWebHostEnvironment env,
        IChildPermitDocumentationPdfRenderer childPermitPdf,
        ILogger<EmailService> logger)
    {
        _db = db;
        _config = config;
        _documentService = documentService;
        _env = env;
        _childPermitPdf = childPermitPdf;
        _logger = logger;
    }

    /// <summary>
    /// All outbound SMTP in this service must flow through <see cref="SendAsync"/> or <see cref="SendWithPartsAsync"/> so this runs once per message.
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

    /// <summary>When dev routing changes the recipient, record the real addressee on the message (Gmail shows custom headers).</summary>
    private static void AddIntendedRecipientHeaderIfRedirected(MailMessage message, string intendedBeforeRouting, string routedTo)
    {
        if (string.Equals(intendedBeforeRouting, routedTo, StringComparison.OrdinalIgnoreCase))
            return;
        var v = (intendedBeforeRouting ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (v.Length > 250)
            v = v[..250];
        if (string.IsNullOrEmpty(v))
            return;
        try
        {
            message.Headers.Remove("X-Tradion-Intended-Recipient");
            message.Headers.Add("X-Tradion-Intended-Recipient", v);
        }
        catch
        {
            // ignore malformed / duplicate header edge cases
        }
    }

    /// <summary>Single canonical closing for every outbound email (matches supplier draft style).</summary>
    private const string ProfessionalSignOff = "Kind regards,\nDa Vinci's Civils & Pumps";

    /// <summary>
    /// Strips trailing sign-off blocks so we never double up: supplier drafts and other templates often already end with
    /// <c>Kind regards,</c> / <c>Kind regards</c> plus the company line, while this service also appends the same closing.
    /// </summary>
    private static string StripTrailingDvcpSignOffs(string normalizedBody)
    {
        var t = normalizedBody.TrimEnd();
        // Allow optional comma after "Kind regards", flexible blank lines before the block.
        var re = new Regex(@"(?:\n)*\s*Kind regards\s*,?\s*\n+\s*Da Vinci's Civils & Pumps\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        cleaned = StripTrailingDvcpSignOffs(cleaned);
        if (string.IsNullOrWhiteSpace(cleaned))
            return ProfessionalSignOff;
        return cleaned + "\n\n" + ProfessionalSignOff;
    }

    private static string BuildBrandedHtmlBody(string subject, string plainBody, string? logoContentId)
    {
        var encodedSubject = WebUtility.HtmlEncode(subject ?? "Tradion Notification");
        var bodyHtml = ConvertPlainTextToHtml(plainBody);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(encodedSubject).Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif;color:#111827;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#f3f4f6;padding:24px 12px;\">");
        sb.Append("<tr><td align=\"center\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:680px;background:#ffffff;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden;\">");
        sb.Append("<tr><td style=\"padding:18px 20px;border-bottom:4px solid #d4b646;background:#ffffff;\">");
        if (!string.IsNullOrWhiteSpace(logoContentId))
        {
            sb.Append("<img src=\"cid:").Append(logoContentId).Append("\" alt=\"Da Vinci's Civils & Pumps\" style=\"max-height:52px;width:auto;display:block;\">");
        }
        else
        {
            sb.Append("<div style=\"font-weight:700;font-size:18px;color:#111827;\">Da Vinci's Civils & Pumps</div>");
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

    private void ApplyBrandedBody(MailMessage message, string subject, string plainBody)
    {
        message.Body = plainBody;
        message.IsBodyHtml = false;
        message.AlternateViews.Clear();

        var plainView = AlternateView.CreateAlternateViewFromString(plainBody, Encoding.UTF8, MediaTypeNames.Text.Plain);
        message.AlternateViews.Add(plainView);

        var logoBytes = PdfTheme.LoadPrimaryLogoBytes(_env);
        string? logoCid = null;
        LinkedResource? logoResource = null;
        if (logoBytes is { Length: > 0 })
        {
            logoCid = "dvcp-logo";
            logoResource = new LinkedResource(new MemoryStream(logoBytes), MediaTypeNames.Image.Png)
            {
                ContentId = logoCid,
                TransferEncoding = TransferEncoding.Base64
            };
        }

        var html = BuildBrandedHtmlBody(subject, plainBody, logoCid);
        var htmlView = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html);
        if (logoResource != null)
            htmlView.LinkedResources.Add(logoResource);
        message.AlternateViews.Add(htmlView);
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
            attachmentBytes = await _documentService.GetQuotePdfAsync(quoteId, ct);
            attachmentName = $"Quote-{quote.QuoteNumber}.pdf";
        }
        try
        {
            return await SendAsync(
            to,
            $"Quote {quote.QuoteNumber}",
            "Please find your quote attached for your review.",
            attachmentBytes,
            attachmentName,
            ct);
        }
        catch (Exception e)
        {

            throw;
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

    public async Task SendPermitDocumentationPackageToClientAsync(Guid jobPermitId, byte[]? primaryPdf, string? primaryPdfFileName, string? toEmail = null, CancellationToken ct = default)
    {
        var permit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == jobPermitId, ct);
        if (permit?.JobCard?.Site?.Company == null)
            return;
        var to = toEmail ?? permit.JobCard.Site.Company.ContactEmail;
        if (string.IsNullOrWhiteSpace(to))
            return;
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
            if (!File.Exists(fullPath)) continue;
            var bytes = await File.ReadAllBytesAsync(fullPath, ct);
            if (bytes.Length == 0) continue;
            var name = string.IsNullOrWhiteSpace(att.FileName) ? Path.GetFileName(rel) : att.FileName;
            parts.Add((bytes, name));
        }

        await SendWithPartsAsync(to, subject, body, parts, ct);
    }

    private bool IsSmtpConfigured(out string? server, out string? fromEmail)
    {
        server = _config["SmtpSettings:Server"];
        fromEmail = _config["SmtpSettings:FromEmail"];
        return !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(fromEmail);
    }

    /// <summary>
    /// Many SMTP relays (e.g. GoDaddy on 587) require credentials when a username is configured.
    /// If the username is set but the password is blank, sending will almost always fail.
    /// </summary>
    private bool TryConfigureSmtpCredentials(SmtpClient client, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
            return true;

        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
        {
            _logger.LogError(
                "Email not sent: SmtpSettings:Password is empty but Username is set. " +
                "Put the SMTP password in appsettings or appsettings.Development.json, or use user secrets, appsettings.Local.json, or environment variable SmtpSettings__Password.");
            return false;
        }

        _logger.LogInformation("SMTP Username: {User}", username);
        _logger.LogInformation("SMTP Password Empty: {Empty}", string.IsNullOrWhiteSpace(password));

        client.UseDefaultCredentials = false;
        client.Credentials = new NetworkCredential(username?.Trim() ?? string.Empty, password);
        return true;
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

        if (!IsSmtpConfigured(out var server, out var fromEmail))
        {
            _logger.LogWarning("Email not sent: SmtpSettings:Server or SmtpSettings:FromEmail is missing.");
            return false;
        }

        var port = _config.GetValue<int>("SmtpSettings:Port", 587);
        var username = _config["SmtpSettings:Username"];
        var password = _config["SmtpSettings:Password"];
        var fromName = _config["SmtpSettings:FromName"];

        using var message = new MailMessage();
        message.From = new MailAddress(fromEmail!, fromName ?? fromEmail);
        message.To.Add(to);
        AddIntendedRecipientHeaderIfRedirected(message, intendedTo, to);
        message.Subject = subject;
        var finalBody = EnsureProfessionalSignOff(body);
        ApplyBrandedBody(message, subject, finalBody);

        if (attachmentBytes != null && attachmentBytes.Length > 0 && !string.IsNullOrWhiteSpace(attachmentName))
        {
            var stream = new MemoryStream(attachmentBytes);
            var attachment = new Attachment(stream, attachmentName, "application/octet-stream");
            message.Attachments.Add(attachment);
        }

        try
        {
            using var client = new SmtpClient(server, port);
            client.EnableSsl = true;
            client.Timeout = 30000;
            if (!TryConfigureSmtpCredentials(client, username ?? string.Empty, password ?? string.Empty))
                return false;

            _logger.LogInformation("Sending SMTP message: subject={Subject}, to={To}, from={From}", subject, to, fromEmail);
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed for message with subject {Subject} to {To}", subject, to);
            return false;
        }
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

        if (!IsSmtpConfigured(out var server, out var fromEmail))
        {
            _logger.LogWarning("Email not sent: SmtpSettings:Server or SmtpSettings:FromEmail is missing.");
            return false;
        }

        var port = _config.GetValue<int>("SmtpSettings:Port", 587);
        var username = _config["SmtpSettings:Username"];
        var password = _config["SmtpSettings:Password"];
        var fromName = _config["SmtpSettings:FromName"];

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

        using var message = new MailMessage();
        message.From = new MailAddress(fromEmail!, fromName ?? fromEmail);
        message.To.Add(to);
        AddIntendedRecipientHeaderIfRedirected(message, intendedTo, to);
        message.Subject = subject;
        var finalBody = EnsureProfessionalSignOff(body);
        ApplyBrandedBody(message, subject, finalBody);

        foreach (var (bytes, fileName) in partsList)
        {
            if (bytes.Length == 0 || string.IsNullOrWhiteSpace(fileName)) continue;
            var stream = new MemoryStream(bytes);
            var mime = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? "application/zip"
                : "application/octet-stream";
            var attachment = new Attachment(stream, fileName, mime);
            message.Attachments.Add(attachment);
        }

        try
        {
            using var client = new SmtpClient(server, port);
            client.EnableSsl = port == 587 || port == 465;
            if (!TryConfigureSmtpCredentials(client, username ?? string.Empty, password ?? string.Empty))
                return false;

            _logger.LogInformation("Sending SMTP message (multipart): subject={Subject}, to={To}, from={From}", subject, to, fromEmail);
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed (multipart) for subject {Subject} to {To}", subject, to);
            return false;
        }
    }
}
