using Ike.Api.Models;

namespace Ike.Api.Services;

public class StatusTransitionService : IStatusTransitionService
{
    public bool TryTransitionQuote(string currentStatus, string requestedStatus, out string normalizedStatus, out string error)
    {
        return TryTransition(
            domainName: "Quote",
            currentStatus,
            requestedStatus,
            isValid: QuoteStatus.IsValid,
            normalize: QuoteStatus.Normalize,
            canTransition: QuoteStatus.CanTransition,
            out normalizedStatus,
            out error);
    }

    public bool TryTransitionPurchaseOrder(string currentStatus, string requestedStatus, out string normalizedStatus, out string error)
    {
        return TryTransition(
            domainName: "PurchaseOrder",
            currentStatus,
            requestedStatus,
            isValid: PurchaseOrderStatus.IsValid,
            normalize: PurchaseOrderStatus.Normalize,
            canTransition: PurchaseOrderStatus.CanTransition,
            out normalizedStatus,
            out error);
    }

    public bool TryTransitionServiceRequest(string currentStatus, string requestedStatus, out string normalizedStatus, out string error)
    {
        return TryTransition(
            domainName: "ServiceRequest",
            currentStatus,
            requestedStatus,
            isValid: ServiceRequestStatus.IsValid,
            normalize: ServiceRequestStatus.Normalize,
            canTransition: ServiceRequestStatus.CanTransition,
            out normalizedStatus,
            out error);
    }

    public bool TryTransitionSupplierQuoteRequest(string currentStatus, string requestedStatus, out string normalizedStatus, out string error)
    {
        return TryTransition(
            domainName: "SupplierQuoteRequest",
            currentStatus,
            requestedStatus,
            isValid: SupplierQuoteRequestStatus.IsValid,
            normalize: SupplierQuoteRequestStatus.Normalize,
            canTransition: SupplierQuoteRequestStatus.CanTransition,
            out normalizedStatus,
            out error);
    }

    private static bool TryTransition(
        string domainName,
        string currentStatus,
        string requestedStatus,
        Func<string?, bool> isValid,
        Func<string, string> normalize,
        Func<string, string, bool> canTransition,
        out string normalizedStatus,
        out string error)
    {
        normalizedStatus = currentStatus;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(requestedStatus))
        {
            error = "Status is required.";
            return false;
        }

        if (!isValid(currentStatus))
        {
            error = $"{domainName} current status '{currentStatus}' is invalid.";
            return false;
        }

        if (!isValid(requestedStatus))
        {
            error = $"{domainName} status '{requestedStatus}' is not valid.";
            return false;
        }

        normalizedStatus = normalize(requestedStatus);
        if (string.Equals(normalize(currentStatus), normalizedStatus, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!canTransition(currentStatus, normalizedStatus))
        {
            error = $"Cannot transition {domainName} from {currentStatus} to {normalizedStatus}.";
            return false;
        }

        return true;
    }
}
