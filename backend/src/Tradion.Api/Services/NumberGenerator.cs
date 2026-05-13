using Tradion.Api.Models;

namespace Tradion.Api.Services;

/// <summary>
/// Generates sequential reference numbers per type and year (e.g. REQ-2025-0001).
/// </summary>
public static class NumberGenerator
{
    public static string NextRequestNumber(IQueryable<ServiceRequest> existing)
    {
        return NextNumber("REQ", existing.Select(sr => sr.RequestNumber));
    }

    public static string NextQuoteNumber(IQueryable<Quote> existing)
    {
        return NextNumber("QUO", existing.Select(q => q.QuoteNumber));
    }

    public static string NextPONumber(IQueryable<PurchaseOrder> existing)
    {
        return NextNumber("PO", existing.Select(po => po.PONumber));
    }

    public static string NextJobCardNumber(IQueryable<JobCard> existing)
    {
        return NextNumber("JC", existing.Select(j => j.JobCardNumber));
    }

    public static string NextInvoiceNumber(IQueryable<Invoice> existing)
    {
        return NextNumber("INV", existing.Select(i => i.InvoiceNumber));
    }

    private static string NextNumber(string prefix, IQueryable<string> existingNumbers)
    {
        var year = DateTime.UtcNow.Year;
        var prefixYear = $"{prefix}-{year}-";
        var maxSeq = 0;
        foreach (var n in existingNumbers.ToList())
        {
            if (n.StartsWith(prefixYear, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(n.AsSpan(prefixYear.Length), out var seq) && seq > maxSeq)
                maxSeq = seq;
        }
        return $"{prefix}-{year}-{(maxSeq + 1):D4}";
    }
}
