using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ike.Api.Services;

public class MicrosoftGraphEmailProvider : IEmailProvider
{
    private const string GraphScope = "https://graph.microsoft.com/.default";
    private const int MaxLoggedResponseLength = 2000;

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MicrosoftGraphEmailProvider> _logger;

    public MicrosoftGraphEmailProvider(HttpClient http, IConfiguration config, ILogger<MicrosoftGraphEmailProvider> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public string ProviderName => "MicrosoftGraph";

    public async Task<bool> SendAsync(EmailMessage email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email.To))
            return false;

        var settings = ReadSettings();
        if (!settings.IsConfigured)
        {
            _logger.LogError("Microsoft Graph email not sent: TenantId, ClientId, ClientSecret, and FromEmail must be configured.");
            return false;
        }

        var token = await GetAccessTokenAsync(settings, ct);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var endpoint = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(settings.FromEmail!)}/sendMail";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(BuildSendMailJson(email), Encoding.UTF8, "application/json");

        try
        {
            _logger.LogInformation(
                "Sending email via Microsoft Graph: subject={Subject}, to={To}, from={From}",
                email.Subject,
                email.To,
                settings.FromEmail);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return true;

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Microsoft Graph sendMail failed: status={StatusCode}, from={From}, to={To}, response={Response}",
                (int)response.StatusCode,
                settings.FromEmail,
                email.To,
                Truncate(responseBody));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Microsoft Graph sendMail failed for subject {Subject} to {To}", email.Subject, email.To);
            return false;
        }
    }

    private MicrosoftGraphSettings ReadSettings()
    {
        return new MicrosoftGraphSettings
        {
            TenantId = _config["MicrosoftGraphEmailSettings:TenantId"]?.Trim(),
            ClientId = _config["MicrosoftGraphEmailSettings:ClientId"]?.Trim(),
            ClientSecret = _config["MicrosoftGraphEmailSettings:ClientSecret"],
            FromEmail = _config["MicrosoftGraphEmailSettings:FromEmail"]?.Trim(),
            FromName = _config["MicrosoftGraphEmailSettings:FromName"]?.Trim()
        };
    }

    private async Task<string?> GetAccessTokenAsync(MicrosoftGraphSettings settings, CancellationToken ct)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{Uri.EscapeDataString(settings.TenantId!)}/oauth2/v2.0/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId!,
                ["client_secret"] = settings.ClientSecret!,
                ["scope"] = GraphScope,
                ["grant_type"] = "client_credentials"
            })
        };

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Microsoft Graph token request failed: status={StatusCode}, tenant={TenantId}, clientId={ClientId}, response={Response}",
                    (int)response.StatusCode,
                    settings.TenantId,
                    settings.ClientId,
                    Truncate(responseBody));
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String)
                return token.GetString();

            _logger.LogError("Microsoft Graph token response did not include access_token.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Microsoft Graph token request failed for tenant {TenantId}, clientId {ClientId}", settings.TenantId, settings.ClientId);
            return null;
        }
    }

    private static string BuildSendMailJson(EmailMessage email)
    {
        var message = new Dictionary<string, object?>
        {
            ["subject"] = email.Subject,
            ["body"] = new Dictionary<string, object?>
            {
                ["contentType"] = "HTML",
                ["content"] = email.HtmlBody
            },
            ["toRecipients"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["emailAddress"] = new Dictionary<string, object?>
                    {
                        ["address"] = email.To
                    }
                }
            }
        };

        var attachments = BuildAttachments(email);
        if (attachments.Count > 0)
            message["attachments"] = attachments;

        var payload = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["saveToSentItems"] = false
        };

        return JsonSerializer.Serialize(payload);
    }

    private static List<Dictionary<string, object?>> BuildAttachments(EmailMessage email)
    {
        var attachments = new List<Dictionary<string, object?>>();

        if (email.InlineLogoBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(email.InlineLogoContentId))
        {
            attachments.Add(new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = email.InlineLogoFileName,
                ["contentType"] = "image/png",
                ["contentId"] = email.InlineLogoContentId,
                ["isInline"] = true,
                ["contentBytes"] = Convert.ToBase64String(email.InlineLogoBytes)
            });
        }

        foreach (var attachment in email.Attachments)
        {
            if (attachment.Bytes.Length == 0 || string.IsNullOrWhiteSpace(attachment.FileName)) continue;
            attachments.Add(new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = attachment.FileName,
                ["contentType"] = attachment.ContentType,
                ["contentBytes"] = Convert.ToBase64String(attachment.Bytes)
            });
        }

        return attachments;
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Length <= MaxLoggedResponseLength ? value : value[..MaxLoggedResponseLength];
    }

    private sealed class MicrosoftGraphSettings
    {
        public string? TenantId { get; init; }
        public string? ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public string? FromEmail { get; init; }
        public string? FromName { get; init; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(TenantId) &&
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ClientSecret) &&
            !string.IsNullOrWhiteSpace(FromEmail);
    }
}
