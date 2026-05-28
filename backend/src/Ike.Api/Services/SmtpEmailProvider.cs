using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace Ike.Api.Services;

public class SmtpEmailProvider : IEmailProvider
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailProvider> _logger;

    public SmtpEmailProvider(IConfiguration config, ILogger<SmtpEmailProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string ProviderName => "Smtp";

    public async Task<bool> SendAsync(EmailMessage email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email.To))
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
        message.To.Add(email.To);
        AddIntendedRecipientHeaderIfRedirected(message, email.IntendedTo, email.To);
        message.Subject = email.Subject;
        ApplyBody(message, email);

        foreach (var attachment in email.Attachments)
        {
            if (attachment.Bytes.Length == 0 || string.IsNullOrWhiteSpace(attachment.FileName)) continue;
            var stream = new MemoryStream(attachment.Bytes);
            message.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
        }

        try
        {
            using var client = new SmtpClient(server, port);
            client.EnableSsl = port == 587 || port == 465;
            client.Timeout = 30000;
            if (!TryConfigureSmtpCredentials(client, username ?? string.Empty, password ?? string.Empty))
                return false;

            _logger.LogInformation("Sending email via SMTP: subject={Subject}, to={To}, from={From}", email.Subject, email.To, fromEmail);
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed for subject {Subject} to {To}", email.Subject, email.To);
            return false;
        }
    }

    private bool IsSmtpConfigured(out string? server, out string? fromEmail)
    {
        server = _config["SmtpSettings:Server"];
        fromEmail = _config["SmtpSettings:FromEmail"];
        return !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(fromEmail);
    }

    private bool TryConfigureSmtpCredentials(SmtpClient client, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
            return true;

        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
        {
            _logger.LogError(
                "Email not sent: SmtpSettings:Password is empty but Username is set. " +
                "Put the SMTP password in appsettings or appsettings.Development.json, user secrets, appsettings.Local.json, or environment variable SmtpSettings__Password.");
            return false;
        }

        _logger.LogInformation("SMTP credentials configured for username {User}.", username);
        client.UseDefaultCredentials = false;
        client.Credentials = new NetworkCredential(username.Trim(), password);
        return true;
    }

    private static void ApplyBody(MailMessage message, EmailMessage email)
    {
        message.Body = email.PlainTextBody;
        message.IsBodyHtml = false;
        message.AlternateViews.Clear();

        var plainView = AlternateView.CreateAlternateViewFromString(email.PlainTextBody, Encoding.UTF8, MediaTypeNames.Text.Plain);
        message.AlternateViews.Add(plainView);

        var htmlView = AlternateView.CreateAlternateViewFromString(email.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);
        if (email.InlineLogoBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(email.InlineLogoContentId))
        {
            var logoResource = new LinkedResource(new MemoryStream(email.InlineLogoBytes), MediaTypeNames.Image.Png)
            {
                ContentId = email.InlineLogoContentId,
                TransferEncoding = TransferEncoding.Base64
            };
            htmlView.LinkedResources.Add(logoResource);
        }
        message.AlternateViews.Add(htmlView);
    }

    private static void AddIntendedRecipientHeaderIfRedirected(MailMessage message, string? intendedBeforeRouting, string routedTo)
    {
        if (string.IsNullOrWhiteSpace(intendedBeforeRouting) ||
            string.Equals(intendedBeforeRouting, routedTo, StringComparison.OrdinalIgnoreCase))
            return;

        var v = intendedBeforeRouting.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (v.Length > 250)
            v = v[..250];
        if (string.IsNullOrEmpty(v))
            return;

        try
        {
            message.Headers.Remove("X-IKE-Intended-Recipient");
            message.Headers.Add("X-IKE-Intended-Recipient", v);
        }
        catch
        {
            // Ignore malformed or duplicate header edge cases.
        }
    }
}
