namespace Ike.Api.Services;

public sealed record EmailAttachment(byte[] Bytes, string FileName, string ContentType = "application/octet-stream");

public sealed class EmailMessage
{
    public string To { get; init; } = string.Empty;
    public string? IntendedTo { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string PlainTextBody { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = Array.Empty<EmailAttachment>();
    public byte[]? InlineLogoBytes { get; init; }
    public string? InlineLogoContentId { get; init; }
    public string InlineLogoFileName { get; init; } = "ike-logo.png";
}
