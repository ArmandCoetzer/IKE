using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class PermissionServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "PermSvc_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    [Fact]
    public async Task GetEffectivePermissionNamesAsync_ReturnsEmpty_WhenUserHasNoRoles()
    {
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());
        await using var db = CreateContext();
        var service = new PermissionService(db, mockUserManager.Object);
        var user = new ApplicationUser { Id = "u1", Email = "u@test.com", UserName = "u@test.com", IsActive = true };

        var permissions = await service.GetEffectivePermissionNamesAsync(user);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task GetUserIdsWithPermissionAsync_ReturnsEmpty_WhenPermissionNotFound()
    {
        await using var db = CreateContext();
        var mockUserManager = CreateMockUserManager();
        var service = new PermissionService(db, mockUserManager.Object);

        var userIds = await service.GetUserIdsWithPermissionAsync("NonExistentPermission", CancellationToken.None);

        Assert.Empty(userIds);
    }
}
