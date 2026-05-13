namespace Tradion.Api.Helpers;

public static class TradionTimeHelper
{
    private static readonly string[] SouthAfricaTimeZoneIds =
    [
        "South Africa Standard Time", // Windows
        "Africa/Johannesburg" // Linux/macOS
    ];

    private static TimeZoneInfo? _cached;

    public static TimeZoneInfo SouthAfricaTimeZone()
    {
        if (_cached != null) return _cached;
        foreach (var id in SouthAfricaTimeZoneIds)
        {
            try
            {
                _cached = TimeZoneInfo.FindSystemTimeZoneById(id);
                return _cached;
            }
            catch
            {
                // try next id
            }
        }

        _cached = TimeZoneInfo.Utc;
        return _cached;
    }

    public static DateTime TodayInSouthAfrica(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, SouthAfricaTimeZone());
        return local.Date;
    }
}
