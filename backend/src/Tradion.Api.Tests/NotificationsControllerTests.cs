using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Notifications;
using Tradion.Api.Models;
using Xunit;

namespace Tradion.Api.Tests;

public class NotificationsControllerTests
{
    private static ApplicationDbContext CreateInMemoryContext(string userId = "user-1")
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "NotifCtrl_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        var n1 = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "N1",
            Body = "B1",
            Type = "T1",
            CreatedAt = DateTime.UtcNow,
            ReadAt = null
        };
        var n2 = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "N2",
            Body = "B2",
            Type = "T2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ReadAt = DateTime.UtcNow
        };
        ctx.Notifications.AddRange(n1, n2);
        ctx.SaveChanges();
        return ctx;
    }

    private static NotificationsController CreateController(ApplicationDbContext db, string userId = "user-1")
    {
        var controller = new NotificationsController(db);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    [Fact]
    public async Task UnreadCount_WhenNoUser_ReturnsUnauthorized()
    {
        await using var db = CreateInMemoryContext();
        var controller = new NotificationsController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.UnreadCount(default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task UnreadCount_ReturnsOnlyUnreadCountForCurrentUser()
    {
        await using var db = CreateInMemoryContext();
        var controller = CreateController(db);

        var result = await controller.UnreadCount(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UnreadCountDto>(ok.Value);
        Assert.Equal(1, dto.Count);
    }

    [Fact]
    public async Task List_WhenNoUser_ReturnsUnauthorized()
    {
        await using var db = CreateInMemoryContext();
        var controller = new NotificationsController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.List(default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task List_ReturnsOnlyCurrentUserNotifications_UnreadFirst()
    {
        await using var db = CreateInMemoryContext();
        var controller = CreateController(db);

        var result = await controller.List(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<NotificationDto>>(ok.Value);
        Assert.Equal(2, list.Count);
        Assert.Null(list[0].ReadAt);
        Assert.NotNull(list[1].ReadAt);
    }

    [Fact]
    public async Task MarkRead_WhenNotificationNotFound_ReturnsNotFound()
    {
        await using var db = CreateInMemoryContext();
        var controller = CreateController(db);
        var nonExistentId = Guid.NewGuid();

        var result = await controller.MarkRead(nonExistentId, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarkRead_WhenNotificationBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var db = CreateInMemoryContext("other-user");
        var nId = (await db.Notifications.FirstAsync()).Id;
        var controller = CreateController(db, "current-user");

        var result = await controller.MarkRead(nId, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarkRead_SetsReadAtAndReturnsNoContent()
    {
        await using var db = CreateInMemoryContext();
        var n = await db.Notifications.FirstAsync(n => n.ReadAt == null);
        var controller = CreateController(db);

        var result = await controller.MarkRead(n.Id, default);

        Assert.IsType<NoContentResult>(result);
        var updated = await db.Notifications.FindAsync(n.Id);
        Assert.NotNull(updated);
        Assert.NotNull(updated.ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_WhenNoUser_ReturnsUnauthorized()
    {
        await using var db = CreateInMemoryContext();
        var controller = new NotificationsController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.MarkAllRead(default);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task MarkAllRead_MarksAllUnreadAsRead()
    {
        await using var db = CreateInMemoryContext();
        var controller = CreateController(db);
        var unreadBefore = await db.Notifications.CountAsync(n => n.ReadAt == null);
        Assert.Equal(1, unreadBefore);

        var result = await controller.MarkAllRead(default);

        Assert.IsType<NoContentResult>(result);
        var unreadAfter = await db.Notifications.CountAsync(n => n.ReadAt == null);
        Assert.Equal(0, unreadAfter);
    }
}
