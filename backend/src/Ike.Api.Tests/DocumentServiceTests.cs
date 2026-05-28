using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ike.Api.Data;
using Ike.Api.Services;
using Xunit;

namespace Ike.Api.Tests;

public class DocumentServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "DocSvc_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IWebHostEnvironment MockEnv()
    {
        var m = new Mock<IWebHostEnvironment>();
        m.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());
        return m.Object;
    }

    [Fact]
    public async Task GetQuotePdfAsync_ReturnsNull_WhenQuoteNotFound()
    {
        await using var db = CreateContext();
        var service = new DocumentService(db, MockEnv());

        var bytes = await service.GetQuotePdfAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task GetInvoicePdfAsync_ReturnsNull_WhenInvoiceNotFound()
    {
        await using var db = CreateContext();
        var service = new DocumentService(db, MockEnv());

        var bytes = await service.GetInvoicePdfAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task GetPurchaseOrderPdfAsync_ReturnsNull_WhenPurchaseOrderNotFound()
    {
        await using var db = CreateContext();
        var service = new DocumentService(db, MockEnv());

        var bytes = await service.GetPurchaseOrderPdfAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(bytes);
    }
}
