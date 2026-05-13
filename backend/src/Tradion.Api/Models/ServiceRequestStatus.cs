namespace Tradion.Api.Models;

public static class ServiceRequestStatus
{
    public const string New = "New";
    public const string Pending = "Pending";
    public const string Open = "Open";
    public const string Scheduled = "Scheduled";
    public const string Closed = "Closed";

    public static readonly string[] All = { New, Pending, Open, Scheduled, Closed };

    public static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [New] = new[] { Pending, Open, Scheduled, Closed },
        [Pending] = new[] { Open, Scheduled, Closed },
        [Open] = new[] { Scheduled, Closed },
        [Scheduled] = new[] { Closed },
        [Closed] = Array.Empty<string>()
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
