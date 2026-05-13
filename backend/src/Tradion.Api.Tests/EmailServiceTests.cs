using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Tradion.Api.Data;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class EmailServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "EmailSvc_" + Guid.NewGuid())
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
    public async Task SendQuoteToClientAsync_CompletesWithoutThrow_WhenQuoteNotFound()
    {
        await using var db = CreateContext();
        var config = new ConfigurationBuilder().Build();
        var mockDoc = new Mock<IDocumentService>();
        var mockChildPdf = new Mock<IChildPermitDocumentationPdfRenderer>();
        var service = new EmailService(db, config, mockDoc.Object, MockEnv(), mockChildPdf.Object);

        await service.SendQuoteToClientAsync(Guid.NewGuid(), null, false, CancellationToken.None);
    }

    [Fact]
    public async Task SendInvoiceToClientAsync_CompletesWithoutThrow_WhenInvoiceNotFound()
    {
        await using var db = CreateContext();
        var config = new ConfigurationBuilder().Build();
        var mockDoc = new Mock<IDocumentService>();
        var mockChildPdf = new Mock<IChildPermitDocumentationPdfRenderer>();
        var service = new EmailService(db, config, mockDoc.Object, MockEnv(), mockChildPdf.Object);

        await service.SendInvoiceToClientAsync(Guid.NewGuid(), null, false, CancellationToken.None);
    }
}
