using Tradion.Api.Models;
using Xunit;

namespace Tradion.Api.Tests;

public class PermitStatusTests
{
    [Theory]
    [InlineData("Active")]
    [InlineData("active")]
    [InlineData("Approved")]
    [InlineData("approved")]
    public void IsActiveLike_Recognizes_Active_And_Approved(string value)
    {
        Assert.True(PermitStatus.IsActiveLike(value));
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Done")]
    [InlineData("closed")]
    [InlineData("done")]
    public void IsClosedLike_Recognizes_Closed_And_Done(string value)
    {
        Assert.True(PermitStatus.IsClosedLike(value));
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    public void IsRejectedOrCancelled_Recognizes_Both(string value)
    {
        Assert.True(PermitStatus.IsRejectedOrCancelled(value));
    }
}
