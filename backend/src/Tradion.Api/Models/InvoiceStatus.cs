namespace Tradion.Api.Models;

public static class InvoiceStatus
{
    public const string Draft = "Draft";
    public const string WaitingPayment = "WaitingPayment";
    public const string Paid = "Paid";

    public static bool IsPaid(string? status) =>
        string.Equals((status ?? "").Trim(), Paid, StringComparison.OrdinalIgnoreCase);
}
