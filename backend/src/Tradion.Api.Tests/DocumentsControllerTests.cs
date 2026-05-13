using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class DocumentsControllerTests
{
    [Fact]
    public async Task GetQuotePdf_ReturnsNotFound_WhenDocumentServiceReturnsNull()
    {
        var docMock = new Mock<IDocumentService>();
        docMock.Setup(s => s.GetQuotePdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("Docs_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        var quoteId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        db.Companies.Add(new Company { Id = companyId, Name = "Test Co", Type = CompanyType.Client });
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            QuoteNumber = "Q-TEST-1",
            CompanyId = companyId,
            SiteId = Guid.NewGuid(),
            Amount = 100,
            Currency = "ZAR",
            Description = "Test quote",
            Status = QuoteStatus.Draft,
            CreatedById = "test-user"
        });
        await db.SaveChangesAsync();
        var envMock = new Mock<IWebHostEnvironment>();
        var emailMock = new Mock<IEmailService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((companyId, true));
        scopeGuardMock.Setup(x => x.CanAccessCompanyAsync(It.IsAny<Guid?>(), It.IsAny<Company?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        scopeGuardMock.Setup(x => x.CanAccessJobCardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = new DocumentsController(docMock.Object, db, envMock.Object, emailMock.Object, currentUserMock.Object, scopeGuardMock.Object);

        var result = await controller.GetQuotePdf(quoteId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetQuotePdf_ReturnsFile_WhenDocumentServiceReturnsBytes()
    {
        var docMock = new Mock<IDocumentService>();
        var pdfId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        docMock.Setup(s => s.GetQuotePdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("Docs2_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        db.Companies.Add(new Company { Id = companyId, Name = "Test Co", Type = CompanyType.Client });
        db.Quotes.Add(new Quote
        {
            Id = pdfId,
            QuoteNumber = "Q-TEST-2",
            CompanyId = companyId,
            SiteId = Guid.NewGuid(),
            Amount = 200,
            Currency = "ZAR",
            Description = "Test quote",
            Status = QuoteStatus.Draft,
            CreatedById = "test-user"
        });
        await db.SaveChangesAsync();
        var envMock = new Mock<IWebHostEnvironment>();
        var emailMock = new Mock<IEmailService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((companyId, true));
        scopeGuardMock.Setup(x => x.CanAccessCompanyAsync(It.IsAny<Guid?>(), It.IsAny<Company?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        scopeGuardMock.Setup(x => x.CanAccessJobCardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = new DocumentsController(docMock.Object, db, envMock.Object, emailMock.Object, currentUserMock.Object, scopeGuardMock.Object);

        var result = await controller.GetQuotePdf(pdfId, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(bytes, file.FileContents);
        docMock.Verify(s => s.GetQuotePdfAsync(pdfId, It.IsAny<CancellationToken>()), Times.Once);
        await db.DisposeAsync();
    }
}
