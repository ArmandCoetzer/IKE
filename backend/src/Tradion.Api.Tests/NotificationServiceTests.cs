using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class NotificationServiceTests
{
    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Notif_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task CreateForUserAsync_AddsOneNotification()
    {
        await using var db = CreateInMemoryContext();
        var permissionService = new Mock<IPermissionService>().Object;
        var sut = new NotificationService(db, permissionService);

        await sut.CreateForUserAsync("user-1", "Title", "Body", "ServiceRequest", "entity-123");

        var list = await db.Notifications.ToListAsync();
        Assert.Single(list);
        var n = list[0];
        Assert.Equal("user-1", n.UserId);
        Assert.Equal("Title", n.Title);
        Assert.Equal("Body", n.Body);
        Assert.Equal("ServiceRequest", n.Type);
        Assert.Equal("entity-123", n.RelatedEntityId);
        Assert.Null(n.ReadAt);
    }

    [Fact]
    public async Task CreateForUserAsync_TruncatesLongTitle()
    {
        await using var db = CreateInMemoryContext();
        var permissionService = new Mock<IPermissionService>().Object;
        var sut = new NotificationService(db, permissionService);
        var longTitle = new string('x', 300);

        await sut.CreateForUserAsync("user-1", longTitle, "Body", "Type", null);

        var n = await db.Notifications.FirstAsync();
        Assert.Equal(256, n.Title.Length);
    }

    [Fact]
    public async Task CreateForUserAsync_TruncatesLongBody()
    {
        await using var db = CreateInMemoryContext();
        var permissionService = new Mock<IPermissionService>().Object;
        var sut = new NotificationService(db, permissionService);
        var longBody = new string('x', 2500);

        await sut.CreateForUserAsync("user-1", "Title", longBody, "Type", null);

        var n = await db.Notifications.FirstAsync();
        Assert.Equal(2000, n.Body.Length);
    }

    [Fact]
    public async Task NotifyUsersWithPermissionAsync_CreatesNotificationForEachUser_ExcludingExcludeUserId()
    {
        await using var db = CreateInMemoryContext();
        var mockPerm = new Mock<IPermissionService>();
        mockPerm.Setup(p => p.GetUserIdsWithPermissionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "u1", "u2", "u3" });
        var sut = new NotificationService(db, mockPerm.Object);

        await sut.NotifyUsersWithPermissionAsync("ViewRequests", "New request", "Body", "ServiceRequest", "sr-1", excludeUserId: "u2", default);

        var list = await db.Notifications.ToListAsync();
        Assert.Equal(2, list.Count);
        var userIds = list.Select(n => n.UserId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "u1", "u3" }, userIds);
    }

    [Fact]
    public async Task NotifyUsersWithPermissionAsync_WhenNoUsersToNotify_DoesNotSave()
    {
        await using var db = CreateInMemoryContext();
        var mockPerm = new Mock<IPermissionService>();
        mockPerm.Setup(p => p.GetUserIdsWithPermissionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var sut = new NotificationService(db, mockPerm.Object);

        await sut.NotifyUsersWithPermissionAsync("ViewRequests", "Title", "Body", "Type", null, null, default);

        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task NotifyUsersWithPermissionAsync_WhenAllUsersExcluded_DoesNotSave()
    {
        await using var db = CreateInMemoryContext();
        var mockPerm = new Mock<IPermissionService>();
        mockPerm.Setup(p => p.GetUserIdsWithPermissionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "only-user" });
        var sut = new NotificationService(db, mockPerm.Object);

        await sut.NotifyUsersWithPermissionAsync("ViewRequests", "Title", "Body", "Type", null, excludeUserId: "only-user", default);

        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task NotifyUsersWithPermissionAsync_EmptyStringExcludeUserId_NotifiesAll()
    {
        await using var db = CreateInMemoryContext();
        var mockPerm = new Mock<IPermissionService>();
        mockPerm.Setup(p => p.GetUserIdsWithPermissionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "u1" });
        var sut = new NotificationService(db, mockPerm.Object);

        await sut.NotifyUsersWithPermissionAsync("ViewRequests", "Title", "Body", "Type", null, excludeUserId: "", default);

        Assert.Equal(1, await db.Notifications.CountAsync());
        Assert.Equal("u1", (await db.Notifications.FirstAsync()).UserId);
    }

    [Fact]
    public async Task NotifyUsersWithPermissionAsync_NullExcludeUserId_NotifiesAll()
    {
        await using var db = CreateInMemoryContext();
        var mockPerm = new Mock<IPermissionService>();
        mockPerm.Setup(p => p.GetUserIdsWithPermissionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "a", "b" });
        var sut = new NotificationService(db, mockPerm.Object);

        await sut.NotifyUsersWithPermissionAsync("ViewReports", "Title", "Body", "OverdueInvoice", null, excludeUserId: null, default);

        Assert.Equal(2, await db.Notifications.CountAsync());
    }
}
