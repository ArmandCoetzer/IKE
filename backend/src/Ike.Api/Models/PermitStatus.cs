namespace Ike.Api.Models;

/// <summary>Permit state machine: Draft → Captured (form saved) → Active (client sign-off) → Expired/Closed. Signed is legacy alias of Captured.</summary>
public static class PermitStatus
{
    public const string Pending = "Pending";
    public const string Draft = "Draft";
    /// <summary>Checklist / structured form saved; awaiting client sign-off.</summary>
    public const string Captured = "Captured";
    /// <summary>Legacy — treat like Captured in UI and transitions.</summary>
    public const string Signed = "Signed";
    public const string Active = "Active";
    public const string Approved = "Approved";
    public const string Expired = "Expired";
    public const string Closed = "Closed";
    public const string Done = "Done";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = { Draft, Captured, Signed, Active, Expired, Closed };

    public static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = new[] { Captured, Signed, Active, Closed },
        [Captured] = new[] { Active, Closed },
        [Signed] = new[] { Active, Closed },
        [Active] = new[] { Expired, Closed },
        [Expired] = new[] { Closed },
        [Closed] = Array.Empty<string>(),
    };

    public static bool IsTerminal(string status) =>
        string.Equals(status, Expired, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Closed, StringComparison.OrdinalIgnoreCase);

    public static bool IsDraftLike(string? status) =>
        string.Equals((status ?? "").Trim(), Draft, StringComparison.OrdinalIgnoreCase);

    public static bool IsCapturedLike(string? status) =>
        string.Equals((status ?? "").Trim(), Captured, StringComparison.OrdinalIgnoreCase)
        || string.Equals((status ?? "").Trim(), Signed, StringComparison.OrdinalIgnoreCase);

    public static bool IsActiveLike(string? status) =>
        string.Equals((status ?? "").Trim(), Active, StringComparison.OrdinalIgnoreCase)
        || string.Equals((status ?? "").Trim(), Approved, StringComparison.OrdinalIgnoreCase);

    public static bool IsExpiredLike(string? status) =>
        string.Equals((status ?? "").Trim(), Expired, StringComparison.OrdinalIgnoreCase);

    public static bool IsClosedLike(string? status) =>
        string.Equals((status ?? "").Trim(), Closed, StringComparison.OrdinalIgnoreCase)
        || string.Equals((status ?? "").Trim(), Done, StringComparison.OrdinalIgnoreCase);

    public static bool IsRejectedOrCancelled(string? status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s is "rejected" or "cancelled";
    }

    public static bool CanTransition(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return false;
        if (!Transitions.TryGetValue(from.Trim(), out var allowed)) return false;
        return allowed.Contains(to.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
