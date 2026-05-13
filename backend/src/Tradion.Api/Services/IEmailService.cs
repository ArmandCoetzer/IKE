namespace Tradion.Api.Services;

public interface IEmailService
{
    Task<bool> SendQuoteToClientAsync(Guid quoteId, string? toEmail = null, bool attachPdf = false, CancellationToken ct = default);
    Task<bool> SendInvoiceToClientAsync(Guid invoiceId, string? toEmail = null, bool attachPdf = false, CancellationToken ct = default);
    Task SendPaymentReminderAsync(Guid invoiceId, string? toEmail = null, bool attachPdf = false, CancellationToken ct = default);
    /// <summary>Email client with primary PDF (e.g. rendered WA) plus uploaded permit files (excluding duplicate signature loose files when PDF provided).</summary>
    Task SendPermitDocumentationPackageToClientAsync(Guid jobPermitId, byte[]? primaryPdf, string? primaryPdfFileName, string? toEmail = null, CancellationToken ct = default);
    Task SendClientInviteEmailAsync(string toEmail, string inviteLink, string? companyName = null, CancellationToken ct = default);
    Task SendEmployeeInviteEmailAsync(string toEmail, string inviteLink, string? fullName = null, CancellationToken ct = default);
    /// <summary>Email the job card summary PDF to the site client (company contact email).</summary>
    Task SendJobCardPdfToClientAsync(Guid jobCardId, string? toEmail = null, CancellationToken ct = default);
    /// <summary>Email supplier to request stock / quote for a part.</summary>
    Task SendSupplierStockRequestAsync(Guid supplierQuoteRequestId, string? toEmail = null, CancellationToken ct = default);
    Task SendCustomEmailAsync(string toEmail, string subject, string body, CancellationToken ct = default);
    Task SendOperationalRiskSummaryAsync(string toEmail, string subject, IReadOnlyList<string> lines, CancellationToken ct = default);
}
