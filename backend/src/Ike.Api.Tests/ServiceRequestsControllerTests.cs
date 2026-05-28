using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Ike.Api.DTOs.ServiceRequests;
using Ike.Api.Models;
using Ike.Api.Services;
using Xunit;

namespace Ike.Api.Tests;

public class ServiceRequestsControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "SR_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoRequests()
    {
        await using var db = CreateContext();
        var mockNotif = new Mock<INotificationService>();
        var controller = new ServiceRequestsController(db, mockNotif.Object);

        var result = await controller.List(null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<ServiceRequestDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenRequestDoesNotExist()
    {
        await using var db = CreateContext();
        var mockNotif = new Mock<INotificationService>();
        var controller = new ServiceRequestsController(db, mockNotif.Object);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsNotFound_WhenRequestDoesNotExist()
    {
        await using var db = CreateContext();
        var mockNotif = new Mock<INotificationService>();
        var controller = new ServiceRequestsController(db, mockNotif.Object);

        var result = await controller.UpdateStatus(Guid.NewGuid(), new UpdateStatusRequest { Status = "Open" });

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
