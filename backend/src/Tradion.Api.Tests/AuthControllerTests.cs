using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Auth;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class AuthControllerTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("AuthCtrl_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AuthController CreateAuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signIn,
        IJwtTokenService jwt,
        IPermissionService perm,
        IConfiguration config,
        string environmentName = "Development")
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(environmentName);
        var email = new Mock<IEmailService>();
        var logger = new Mock<ILogger<AuthController>>();
        return new AuthController(
            userManager,
            signIn,
            jwt,
            perm,
            config,
            CreateDb(),
            logger.Object,
            email.Object,
            env.Object);
    }

    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
    }

    private static Mock<SignInManager<ApplicationUser>> CreateMockSignInManager(UserManager<ApplicationUser> userManager)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return new Mock<SignInManager<ApplicationUser>>(
            userManager,
            contextAccessor.Object,
            claimsFactory.Object,
            null, null, null, null);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUserNotFound()
    {
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        var mockSignIn = CreateMockSignInManager(mockUserManager.Object);
        var mockJwt = new Mock<IJwtTokenService>();
        var mockPerm = new Mock<IPermissionService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ExpiryMinutes"] = "60" }).Build();
        var controller = CreateAuthController(mockUserManager.Object, mockSignIn.Object, mockJwt.Object, mockPerm.Object, config);

        var result = await controller.Login(new LoginRequest { Email = "nobody@test.com", Password = "pass" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.NotNull(unauthorized.Value);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenEmailAlreadyExists()
    {
        var mockUserManager = CreateMockUserManager();
        var mockSignIn = CreateMockSignInManager(mockUserManager.Object);
        var mockJwt = new Mock<IJwtTokenService>();
        var mockPerm = new Mock<IPermissionService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ExpiryMinutes"] = "60" }).Build();
        mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Email already exists." }));
        var controller = CreateAuthController(mockUserManager.Object, mockSignIn.Object, mockJwt.Object, mockPerm.Object, config, "Testing");

        var result = await controller.Register(new RegisterRequest
        {
            Email = "existing@test.com",
            Password = "Pass123!",
            FullName = "Test",
            CompanyName = "Co"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task Me_ReturnsUnauthorized_WhenNoUserInContext()
    {
        var mockUserManager = CreateMockUserManager();
        var mockSignIn = CreateMockSignInManager(mockUserManager.Object);
        var mockJwt = new Mock<IJwtTokenService>();
        var mockPerm = new Mock<IPermissionService>();
        var config = new ConfigurationBuilder().Build();
        var controller = CreateAuthController(mockUserManager.Object, mockSignIn.Object, mockJwt.Object, mockPerm.Object, config);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await controller.Me();

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void Logout_ReturnsNoContent()
    {
        var mockUserManager = CreateMockUserManager();
        var mockSignIn = CreateMockSignInManager(mockUserManager.Object);
        var mockJwt = new Mock<IJwtTokenService>();
        var mockPerm = new Mock<IPermissionService>();
        var config = new ConfigurationBuilder().Build();
        var controller = CreateAuthController(mockUserManager.Object, mockSignIn.Object, mockJwt.Object, mockPerm.Object, config);

        var result = controller.Logout();

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUserHasInvitedStatus()
    {
        var invitedUser = new ApplicationUser
        {
            Id = "u1",
            Email = "invited@test.com",
            UserName = "invited@test.com",
            IsActive = true,
            RegistrationStatus = SeedData.RegistrationStatusInvited
        };
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(invitedUser);
        var mockSignIn = CreateMockSignInManager(mockUserManager.Object);
        mockSignIn.Setup(x => x.CheckPasswordSignInAsync(invitedUser, It.IsAny<string>(), false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        var mockJwt = new Mock<IJwtTokenService>();
        var mockPerm = new Mock<IPermissionService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ExpiryMinutes"] = "60" }).Build();
        var controller = CreateAuthController(mockUserManager.Object, mockSignIn.Object, mockJwt.Object, mockPerm.Object, config);

        var result = await controller.Login(new LoginRequest { Email = "invited@test.com", Password = "Pass123!" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.NotNull(unauthorized.Value);
    }
}
