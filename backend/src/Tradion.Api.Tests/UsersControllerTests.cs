using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Users;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class UsersControllerTests
{
    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Users_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static (Mock<IEmailService>, IConfiguration, Mock<ICurrentUserService>) CreateMocks()
    {
        var emailMock = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().Build();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Guid?)null, false));
        return (emailMock, config, currentUserMock);
    }

    private static UsersController CreateController(Mock<UserManager<ApplicationUser>> userManager, ApplicationDbContext db, Mock<IEmailService> emailMock, IConfiguration config, Mock<ICurrentUserService>? currentUserMock = null)
    {
        currentUserMock ??= new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Guid?)null, false));
        return new UsersController(userManager.Object, db, emailMock.Object, config, currentUserMock.Object);
    }

    [Fact]
    public void GetRoles_ReturnsAllRoles()
    {
        var mockUserManager = CreateMockUserManager();
        using var db = CreateContext();
        var (emailMock, config, _) = CreateMocks();
        var controller = CreateController(mockUserManager, db, emailMock, config);

        var result = controller.GetRoles();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<string>>(ok.Value);
        Assert.Equal(5, list.Count);
        Assert.Contains("Admin", list);
        Assert.Contains("Manager", list);
        Assert.Contains("Client", list);
    }

    [Fact]
    public void GetRoles_ExcludeClient_ReturnsFourRolesWithoutClient()
    {
        var mockUserManager = CreateMockUserManager();
        using var db = CreateContext();
        var (emailMock, config, _) = CreateMocks();
        var controller = CreateController(mockUserManager, db, emailMock, config);

        var result = controller.GetRoles(excludeClient: true);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<string>>(ok.Value);
        Assert.Equal(4, list.Count);
        Assert.DoesNotContain("Client", list);
        Assert.Contains("Admin", list);
        Assert.Contains("Manager", list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        await using var db = CreateContext();
        var (emailMock, config, _) = CreateMocks();
        var controller = CreateController(mockUserManager, db, emailMock, config);

        var result = await controller.Get("nonexistent-id");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenInvalidRole()
    {
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        await using var db = CreateContext();
        var (emailMock, config, _) = CreateMocks();
        var controller = CreateController(mockUserManager, db, emailMock, config);
        var request = new CreateUserRequest { Email = "new@test.com", FirstName = "A", LastName = "B", Password = "Pass123!", Role = "InvalidRole" };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenRoleIsClient()
    {
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        await using var db = CreateContext();
        var (emailMock, config, _) = CreateMocks();
        var controller = CreateController(mockUserManager, db, emailMock, config);
        var request = new CreateUserRequest { Email = "new@test.com", FirstName = "A", LastName = "B", Password = "Pass123!", Role = SeedData.RoleClient };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
    }
}
