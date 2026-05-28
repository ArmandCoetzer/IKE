namespace Ike.Api.Models;

public static class SupplierQuoteRequestStatus
{
    public const string Requested = "Requested";
    public const string Quoted = "Quoted";
    public const string Ordered = "Ordered";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = { Requested, Quoted, Ordered, Cancelled };

    public static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [Requested] = new[] { Quoted, Cancelled },
        [Quoted] = new[] { Ordered, Cancelled },
        [Ordered] = Array.Empty<string>(),
        [Cancelled] = Array.Empty<string>()
    };

    public static bool IsValid(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && All.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string status) =>
        All.First(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool CanTransition(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return false;
        if (!Transitions.TryGetValue(from.Trim(), out var allowed)) return false;
        return allowed.Contains(to.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
