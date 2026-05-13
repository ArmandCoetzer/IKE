using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.JobCards;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class JobCardsControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "JC_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static JobCardsController CreateController(ApplicationDbContext db)
    {
        var mockCurrentUser = new Mock<ICurrentUserService>();
        mockCurrentUser.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(((Guid?)null, false));
        var mockNotif = new Mock<INotificationService>();
        return new JobCardsController(db, mockCurrentUser.Object, mockNotif.Object);
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoJobCards()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.List(null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<System.Collections.IList>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenJobCardDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsNotFound_WhenJobCardDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.UpdateStatus(Guid.NewGuid(), new UpdateJobCardStatusRequest { Status = "Open" });

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
