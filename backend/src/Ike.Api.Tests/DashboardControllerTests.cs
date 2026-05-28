using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Xunit;

namespace Ike.Api.Tests;

public class DashboardControllerTests
{
    private static ApplicationDbContext CreateEmptyContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Dashboard_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task GetCounts_ReturnsDtoWithAllCountsZero_WhenDatabaseEmpty()
    {
        await using var db = CreateEmptyContext();
        var controller = new DashboardController(db);

        var result = await controller.GetCounts();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DashboardCountsDto>(ok.Value);
        Assert.Equal(0, dto.UnprocessedRequests);
        Assert.Equal(0, dto.OngoingJobCards);
        Assert.Equal(0, dto.OverdueInvoices);
        Assert.Equal(0, dto.RequestsWithoutJobCard);
        Assert.Equal(0, dto.CompletedJobsWithoutInvoice);
        Assert.Equal(0, dto.LowStockPartsCount);
    }
}
