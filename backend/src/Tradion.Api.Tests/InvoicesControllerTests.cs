using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Invoices;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class InvoicesControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Invoices_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoInvoices()
    {
        await using var db = CreateContext();
        var emailMock = new Mock<IEmailService>();
        var notifMock = new Mock<INotificationService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        scopeGuardMock.Setup(x => x.CanAccessCompanyAsync(It.IsAny<Guid?>(), It.IsAny<Company?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = new InvoicesController(db, emailMock.Object, notifMock.Object, currentUserMock.Object, scopeGuardMock.Object);

        var result = await controller.List(null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<InvoiceDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenInvoiceDoesNotExist()
    {
        await using var db = CreateContext();
        var emailMock = new Mock<IEmailService>();
        var notifMock = new Mock<INotificationService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        var controller = new InvoicesController(db, emailMock.Object, notifMock.Object, currentUserMock.Object, scopeGuardMock.Object);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
