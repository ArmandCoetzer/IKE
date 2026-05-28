namespace Ike.Api.Services;

public interface IStatusTransitionService
{
    bool TryTransitionQuote(string currentStatus, string requestedStatus, out string normalizedStatus, out string error);
    bool TryTransitionPurchaseOrder(string currentStatus, string requestedStatus, out string normalizedStatus, out string error);
    bool TryTransitionServiceRequest(string currentStatus, string requestedStatus, out string normalizedStatus, out string error);
    bool TryTransitionSupplierQuoteRequest(string currentStatus, string requestedStatus, out string normalizedStatus, out string error);
}
