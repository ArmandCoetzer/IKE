using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Invoices;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class ReportsControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Reports_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static ReportsController CreateController(ApplicationDbContext db)
    {
        var mockUser = new Mock<ICurrentUserService>();
        mockUser.Setup(u => u.GetClientScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync((null, false));
        var mockDoc = new Mock<IDocumentService>();
        return new ReportsController(db, mockUser.Object, mockDoc.Object);
    }

    [Fact]
    public async Task InvoicesByPeriod_ReturnsEmptyList_WhenNoInvoices()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.InvoicesByPeriod(null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<InvoiceDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task InvoicesByPeriod_ReturnsOk_WithFromToParams()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);
        var from = DateTime.UtcNow.Date.AddDays(-7);
        var to = DateTime.UtcNow.Date;

        var result = await controller.InvoicesByPeriod(from, to);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<InvoiceDto>>(ok.Value);
        Assert.Empty(list);
    }
}
