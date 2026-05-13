namespace Tradion.Api.Models;

public static class JobCardStatus
{
    public const string Draft = "Draft";
    public const string Open = "Open";
    public const string InProgress = "In Progress";
    public const string InProgressCompact = "InProgress";
    public const string Scheduled = "Scheduled";
    public const string Completed = "Completed";
    public const string Done = "Done";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";

    public static bool IsDraftLike(string? status) =>
        string.Equals((status ?? "").Trim(), Draft, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenLike(string? status) =>
        string.Equals((status ?? "").Trim(), Open, StringComparison.OrdinalIgnoreCase);

    public static bool IsInProgressLike(string? status)
    {
        var s = (status ?? "").Trim();
        return string.Equals(s, InProgress, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, InProgressCompact, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCompletedLike(string? status)
    {
        var s = (status ?? "").Trim();
        return string.Equals(s, Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, Done, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, Closed, StringComparison.OrdinalIgnoreCase);
    }
}
