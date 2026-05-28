using Ike.Api.Helpers;
using Xunit;

namespace Ike.Api.Tests;

public class IkeTimeHelperTests
{
    [Fact]
    public void TodayInSouthAfrica_Uses_SA_Calendar_Day_Cutoff()
    {
        // 2026-04-07 22:30 UTC => 2026-04-08 00:30 in South Africa (UTC+2)
        var utc = new DateTime(2026, 4, 7, 22, 30, 0, DateTimeKind.Utc);
        var saDate = IkeTimeHelper.TodayInSouthAfrica(utc);
        Assert.Equal(new DateTime(2026, 4, 8), saDate);
    }

    [Fact]
    public void SouthAfricaTimeZone_Returns_A_TimeZone()
    {
        var tz = IkeTimeHelper.SouthAfricaTimeZone();
        Assert.NotNull(tz);
        Assert.False(string.IsNullOrWhiteSpace(tz.Id));
    }
}
