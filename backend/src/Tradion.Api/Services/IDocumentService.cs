using Tradion.Api.Controllers;

namespace Tradion.Api.Services;

public interface IDocumentService
{
    Task<byte[]?> GetQuotePdfAsync(Guid quoteId, CancellationToken ct = default);
    Task<byte[]?> GetInvoicePdfAsync(Guid invoiceId, CancellationToken ct = default);
    Task<byte[]?> GetPurchaseOrderPdfAsync(Guid purchaseOrderId, CancellationToken ct = default);
    Task<byte[]?> GetJobCardPdfAsync(Guid jobCardId, CancellationToken ct = default);
    Task<byte[]> GetProgressReportPdfAsync(ProgressReportDto report, CancellationToken ct = default);
}
