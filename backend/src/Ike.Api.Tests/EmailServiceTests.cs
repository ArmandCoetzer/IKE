using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ike.Api.Data;
using Ike.Api.Models;
using Ike.Api.Services;
using Xunit;

namespace Ike.Api.Tests;

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

    private static IWebHostEnvironment MockEnv(string? contentRoot = null)
    {
        var m = new Mock<IWebHostEnvironment>();
        m.Setup(e => e.ContentRootPath).Returns(contentRoot ?? Directory.GetCurrentDirectory());
        return m.Object;
    }

    private static IEmailProvider MockProvider()
    {
        var m = new Mock<IEmailProvider>();
        m.SetupGet(p => p.ProviderName).Returns("Test");
        m.Setup(p => p.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return m.Object;
    }

    [Fact]
    public async Task SendQuoteToClientAsync_CompletesWithoutThrow_WhenQuoteNotFound()
    {
        await using var db = CreateContext();
        var config = new ConfigurationBuilder().Build();
        var mockDoc = new Mock<IDocumentService>();
        var mockChildPdf = new Mock<IChildPermitDocumentationPdfRenderer>();
        var service = new EmailService(db, config, mockDoc.Object, MockEnv(), mockChildPdf.Object, MockProvider(), NullLogger<EmailService>.Instance);

        await service.SendQuoteToClientAsync(Guid.NewGuid(), null, false, CancellationToken.None);
    }

    [Fact]
    public async Task SendInvoiceToClientAsync_CompletesWithoutThrow_WhenInvoiceNotFound()
    {
        await using var db = CreateContext();
        var config = new ConfigurationBuilder().Build();
        var mockDoc = new Mock<IDocumentService>();
        var mockChildPdf = new Mock<IChildPermitDocumentationPdfRenderer>();
        var service = new EmailService(db, config, mockDoc.Object, MockEnv(), mockChildPdf.Object, MockProvider(), NullLogger<EmailService>.Instance);

        await service.SendInvoiceToClientAsync(Guid.NewGuid(), null, false, CancellationToken.None);
    }

    [Fact]
    public async Task SendQuoteToClientAsync_AttachesOriginalFile_WhenQuoteIsUploaded()
    {
        var root = Path.Combine(Path.GetTempPath(), "ike-email-test-" + Guid.NewGuid().ToString("N"));
        var uploadDir = Path.Combine(root, "uploads", "quotes");
        Directory.CreateDirectory(uploadDir);
        var fileBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(Path.Combine(uploadDir, "uploaded.pdf"), fileBytes);

        try
        {
            await using var db = CreateContext();
            var companyId = Guid.NewGuid();
            var quoteId = Guid.NewGuid();
            db.Companies.Add(new Company
            {
                Id = companyId,
                Name = "Client",
                Type = CompanyType.Client,
                ContactEmail = "client@example.com",
                IsActive = true
            });
            db.Quotes.Add(new Quote
            {
                Id = quoteId,
                QuoteNumber = "QUO-UPLOAD",
                CompanyId = companyId,
                SiteId = Guid.NewGuid(),
                Amount = 100,
                Currency = "ZAR",
                Description = "Uploaded quote",
                Status = QuoteStatus.Draft,
                CreatedById = "test-user",
                IsUploaded = true,
                UploadedFilePath = "uploads/quotes/uploaded.pdf",
                UploadedFileName = "supplier-original.pdf",
                UploadedContentType = "application/pdf"
            });
            await db.SaveChangesAsync();

            EmailMessage? sentEmail = null;
            var provider = new Mock<IEmailProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Test");
            provider.Setup(p => p.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Callback<EmailMessage, CancellationToken>((email, _) => sentEmail = email)
                .ReturnsAsync(true);
            var mockDoc = new Mock<IDocumentService>();
            var mockChildPdf = new Mock<IChildPermitDocumentationPdfRenderer>();
            var service = new EmailService(db, new ConfigurationBuilder().Build(), mockDoc.Object, MockEnv(root), mockChildPdf.Object, provider.Object, NullLogger<EmailService>.Instance);

            var sent = await service.SendQuoteToClientAsync(quoteId, null, true, CancellationToken.None);

            Assert.True(sent);
            Assert.NotNull(sentEmail);
            var attachment = Assert.Single(sentEmail!.Attachments);
            Assert.Equal("supplier-original.pdf", attachment.FileName);
            Assert.Equal(fileBytes, attachment.Bytes);
            mockDoc.Verify(x => x.GetQuotePdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
