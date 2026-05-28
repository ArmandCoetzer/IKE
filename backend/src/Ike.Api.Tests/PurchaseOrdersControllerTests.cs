using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Ike.Api.DTOs.PurchaseOrders;
using Ike.Api.Models;
using Ike.Api.Services;
using Xunit;

namespace Ike.Api.Tests;

public class PurchaseOrdersControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "POCtrl_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static PurchaseOrdersController CreateController(ApplicationDbContext db)
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());
        var currentUserMock = new Mock<ICurrentUserService>();
        var transitionsMock = new Mock<IStatusTransitionService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        scopeGuardMock.Setup(x => x.CanAccessCompanyAsync(It.IsAny<Guid?>(), It.IsAny<Company?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return new PurchaseOrdersController(db, mockEnv.Object, currentUserMock.Object, transitionsMock.Object, scopeGuardMock.Object);
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoPurchaseOrders()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.List(null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PurchaseOrderDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenPurchaseOrderDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsNotFound_WhenPurchaseOrderDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.UpdateStatus(Guid.NewGuid(), new UpdatePurchaseOrderStatusRequest { Status = "Approved" }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenClientOrSiteNotFound()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);
        var request = new CreatePurchaseOrderRequest
        {
            ClientId = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Amount = 100,
            Currency = "ZAR"
        };

        var result = await controller.Create(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Client or site not found.", badRequest.Value);
    }
}
