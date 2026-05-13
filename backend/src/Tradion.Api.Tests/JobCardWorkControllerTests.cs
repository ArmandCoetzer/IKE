using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class JobCardWorkControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "JobCardWork_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenJobCardDoesNotExist()
    {
        await using var db = CreateContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(x => x.ContentRootPath).Returns(Path.GetTempPath());
        var notificationMock = new Mock<INotificationService>();
        var auditMock = new Mock<IAuditService>();
        var realtimeMock = new Mock<IRealtimeHub>();
        var permissionMock = new Mock<IPermissionService>();
        var workAuthRulesMock = new Mock<IWorkAuthorizationPermitRulesService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        scopeGuardMock.Setup(x => x.CanAccessJobCardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = new JobCardWorkController(
            db,
            currentUserMock.Object,
            userManager.Object,
            envMock.Object,
            notificationMock.Object,
            auditMock.Object,
            realtimeMock.Object,
            permissionMock.Object,
            workAuthRulesMock.Object,
            scopeGuardMock.Object);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
