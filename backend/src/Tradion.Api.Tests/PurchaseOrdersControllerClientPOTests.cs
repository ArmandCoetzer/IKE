using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class PurchaseOrdersControllerClientPOTests
{
    private static (ApplicationDbContext Db, Guid PoId) CreateDbWithOnePO()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "PO_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        var poId = Guid.NewGuid();
        var po = new PurchaseOrder
        {
            Id = poId,
            PONumber = "PO-001",
            ClientId = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            CreatedById = "user-1",
            Amount = 100,
            Currency = "ZAR",
            Status = "Draft",
            CreatedAt = DateTime.UtcNow
        };
        db.PurchaseOrders.Add(po);
        db.SaveChanges();
        return (db, poId);
    }

    private static PurchaseOrdersController CreateController(ApplicationDbContext db, string contentRoot = null)
    {
        contentRoot ??= Path.Combine(Path.GetTempPath(), "TradionTests_" + Guid.NewGuid().ToString("N"));
        var mockEnv = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(contentRoot);
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
    public async Task UploadClientPO_WhenFileIsNull_ReturnsBadRequest()
    {
        var (db, poId) = CreateDbWithOnePO();
        await using (db)
        {
            var controller = CreateController(db);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(poId, null!);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No file provided.", badRequest.Value);
        }
    }

    [Fact]
    public async Task UploadClientPO_WhenFileLengthIsZero_ReturnsBadRequest()
    {
        var (db, poId) = CreateDbWithOnePO();
        await using (db)
        {
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(0L);
            mockFile.Setup(f => f.FileName).Returns("doc.pdf");
            var controller = CreateController(db);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(poId, mockFile.Object);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No file provided.", badRequest.Value);
        }
    }

    [Fact]
    public async Task UploadClientPO_WhenFileTooLarge_ReturnsBadRequest()
    {
        var (db, poId) = CreateDbWithOnePO();
        await using (db)
        {
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(11 * 1024 * 1024L); // 11 MB
            mockFile.Setup(f => f.FileName).Returns("large.pdf");
            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var controller = CreateController(db);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(poId, mockFile.Object);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("File too large (max 10 MB).", badRequest.Value);
        }
    }

    [Fact]
    public async Task UploadClientPO_WhenExtensionNotAllowed_ReturnsBadRequest()
    {
        var (db, poId) = CreateDbWithOnePO();
        await using (db)
        {
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(100);
            mockFile.Setup(f => f.FileName).Returns("file.exe");
            var controller = CreateController(db);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(poId, mockFile.Object);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Allowed types: PDF, PNG, JPG.", badRequest.Value);
        }
    }

    [Fact]
    public async Task UploadClientPO_WhenPurchaseOrderNotFound_ReturnsNotFound()
    {
        var (db, _) = CreateDbWithOnePO();
        await using (db)
        {
            var nonExistentId = Guid.NewGuid();
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(100);
            mockFile.Setup(f => f.FileName).Returns("doc.pdf");
            var controller = CreateController(db);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(nonExistentId, mockFile.Object);

            Assert.IsType<NotFoundResult>(result);
        }
    }

    [Fact]
    public async Task UploadClientPO_ValidPdf_ReturnsNoContentAndSetsClientPOFilePath()
    {
        var (db, poId) = CreateDbWithOnePO();
        var tempDir = Path.Combine(Path.GetTempPath(), "TradionTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(100);
            mockFile.Setup(f => f.FileName).Returns("client-po.pdf");
            var ms = new MemoryStream(Encoding.UTF8.GetBytes("fake pdf content"));
            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback<Stream, CancellationToken>((s, _) => ms.CopyTo(s))
                .Returns(Task.CompletedTask);
            var controller = CreateController(db, tempDir);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(poId, mockFile.Object);

            var noContent = Assert.IsType<NoContentResult>(result);
            var po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poId);
            Assert.NotNull(po);
            Assert.NotNull(po.ClientPOFilePath);
            Assert.Contains("uploads/client-po", po.ClientPOFilePath);
            Assert.EndsWith(".pdf", po.ClientPOFilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task UploadClientPO_ValidJpg_ReturnsNoContentAndSetsClientPOFilePath()
    {
        var (db, poId) = CreateDbWithOnePO();
        var tempDir = Path.Combine(Path.GetTempPath(), "TradionTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(500);
            mockFile.Setup(f => f.FileName).Returns("client-po.jpg");
            var ms = new MemoryStream(Encoding.UTF8.GetBytes("fake image"));
            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback<Stream, CancellationToken>((s, _) => ms.CopyTo(s))
                .Returns(Task.CompletedTask);
            var controller = CreateController(db, tempDir);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            var result = await controller.UploadClientPO(poId, mockFile.Object);

            Assert.IsType<NoContentResult>(result);
            var po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poId);
            Assert.NotNull(po);
            Assert.NotNull(po.ClientPOFilePath);
            Assert.EndsWith(".jpg", po.ClientPOFilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }
}
