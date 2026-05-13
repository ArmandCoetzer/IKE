using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Invite;
using Tradion.Api.Models;
using Xunit;

namespace Tradion.Api.Tests;

public class InviteControllerTests
{
    private static (ApplicationDbContext, UserManager<ApplicationUser>) CreateContextAndUserManager()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Invite_" + Guid.NewGuid())
            .Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(context);
        var userManager = new UserManager<ApplicationUser>(
            store, null!, null!, null!, null!, null!, null!, null!, null!);
        return (context, userManager);
    }

    [Fact]
    public async Task GetInviteInfo_ReturnsBadRequest_WhenTokenMissing()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);

            var result = await controller.GetInviteInfo(null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task GetInviteInfo_ReturnsBadRequest_WhenTokenEmpty()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);

            var result = await controller.GetInviteInfo("   ");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task GetInviteInfo_ReturnsNotFound_WhenTokenInvalid()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);

            var result = await controller.GetInviteInfo("nonexistent-token");

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task GetInviteInfo_ReturnsBadRequest_WhenTokenExpired()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var role = new IdentityRole(SeedData.RoleClient) { Id = "r1", NormalizedName = "CLIENT" };
            context.Roles.Add(role);
            var expiredUser = new ApplicationUser
            {
                Id = "u1",
                Email = "expired@test.com",
                UserName = "expired@test.com",
                NormalizedEmail = "EXPIRED@TEST.COM",
                NormalizedUserName = "EXPIRED@TEST.COM",
                InviteToken = "expired-token",
                InviteTokenExpiry = DateTime.UtcNow.AddDays(-1)
            };
            context.Users.Add(expiredUser);
            context.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string> { UserId = expiredUser.Id, RoleId = role.Id });
            await context.SaveChangesAsync();

            var controller = new InviteController(userManager);

            var result = await controller.GetInviteInfo("expired-token");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task GetInviteInfo_ReturnsOk_WhenValidClientToken()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var role = new IdentityRole(SeedData.RoleClient) { Id = "r1", NormalizedName = "CLIENT" };
            context.Roles.Add(role);
            var clientUser = new ApplicationUser
            {
                Id = "u1",
                Email = "client@test.com",
                UserName = "client@test.com",
                NormalizedEmail = "CLIENT@TEST.COM",
                NormalizedUserName = "CLIENT@TEST.COM",
                InviteToken = "valid-client-token",
                InviteTokenExpiry = DateTime.UtcNow.AddDays(7)
            };
            context.Users.Add(clientUser);
            context.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string> { UserId = clientUser.Id, RoleId = role.Id });
            await context.SaveChangesAsync();

            var controller = new InviteController(userManager);

            var result = await controller.GetInviteInfo("valid-client-token");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<InviteInfoDto>(ok.Value);
            Assert.Equal("client", dto.Type);
            Assert.Equal("client@test.com", dto.Email);
        }
    }

    [Fact]
    public async Task GetInviteInfo_ReturnsOk_WhenValidEmployeeToken()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var role = new IdentityRole(SeedData.RoleAdmin) { Id = "r1", NormalizedName = "ADMIN" };
            context.Roles.Add(role);
            var employeeUser = new ApplicationUser
            {
                Id = "u2",
                Email = "employee@test.com",
                UserName = "employee@test.com",
                NormalizedEmail = "EMPLOYEE@TEST.COM",
                NormalizedUserName = "EMPLOYEE@TEST.COM",
                InviteToken = "valid-emp-token",
                InviteTokenExpiry = DateTime.UtcNow.AddDays(7)
            };
            context.Users.Add(employeeUser);
            context.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string> { UserId = employeeUser.Id, RoleId = role.Id });
            await context.SaveChangesAsync();

            var controller = new InviteController(userManager);

            var result = await controller.GetInviteInfo("valid-emp-token");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<InviteInfoDto>(ok.Value);
            Assert.Equal("employee", dto.Type);
            Assert.Equal("employee@test.com", dto.Email);
        }
    }

    [Fact]
    public async Task CompleteInvite_ReturnsBadRequest_WhenTokenMissing()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);
            var request = new CompleteInviteRequest { Token = "", Password = "Pass12345!", ConfirmPassword = "Pass12345!" };

            var result = await controller.CompleteInvite(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task CompleteInvite_ReturnsBadRequest_WhenPasswordMismatch()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);
            var request = new CompleteInviteRequest { Token = "t", Password = "Pass12345!", ConfirmPassword = "Other12345!" };

            var result = await controller.CompleteInvite(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task CompleteInvite_ReturnsBadRequest_WhenPasswordTooShort()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);
            var request = new CompleteInviteRequest { Token = "t", Password = "short", ConfirmPassword = "short" };

            var result = await controller.CompleteInvite(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task CompleteInvite_ReturnsNotFound_WhenTokenInvalid()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var controller = new InviteController(userManager);
            var request = new CompleteInviteRequest { Token = "bad-token", Password = "Pass12345!", ConfirmPassword = "Pass12345!" };

            var result = await controller.CompleteInvite(request);

            Assert.IsType<NotFoundObjectResult>(result);
        }
    }

    [Fact]
    public async Task CompleteInvite_ReturnsBadRequest_WhenClientMissingFirstName()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var role = new IdentityRole(SeedData.RoleClient) { Id = "r1", NormalizedName = "CLIENT" };
            context.Roles.Add(role);
            var clientUser = new ApplicationUser
            {
                Id = "u1",
                Email = "c@test.com",
                UserName = "c@test.com",
                NormalizedEmail = "C@TEST.COM",
                NormalizedUserName = "C@TEST.COM",
                InviteToken = "tok",
                InviteTokenExpiry = DateTime.UtcNow.AddDays(7)
            };
            context.Users.Add(clientUser);
            context.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string> { UserId = clientUser.Id, RoleId = role.Id });
            await context.SaveChangesAsync();

            var controller = new InviteController(userManager);
            var request = new CompleteInviteRequest
            {
                Token = "tok",
                FirstName = "",
                LastName = "Last",
                Password = "Pass12345!",
                ConfirmPassword = "Pass12345!"
            };

            var result = await controller.CompleteInvite(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }
    }

    [Fact]
    public async Task CompleteInvite_ReturnsBadRequest_WhenClientMissingLastName()
    {
        var (context, userManager) = CreateContextAndUserManager();
        await using (context)
        {
            var role = new IdentityRole(SeedData.RoleClient) { Id = "r1", NormalizedName = "CLIENT" };
            context.Roles.Add(role);
            var clientUser = new ApplicationUser
            {
                Id = "u1",
                Email = "c@test.com",
                UserName = "c@test.com",
                NormalizedEmail = "C@TEST.COM",
                NormalizedUserName = "C@TEST.COM",
                InviteToken = "tok",
                InviteTokenExpiry = DateTime.UtcNow.AddDays(7)
            };
            context.Users.Add(clientUser);
            context.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string> { UserId = clientUser.Id, RoleId = role.Id });
            await context.SaveChangesAsync();

            var controller = new InviteController(userManager);
            var request = new CompleteInviteRequest
            {
                Token = "tok",
                FirstName = "First",
                LastName = "  ",
                Password = "Pass12345!",
                ConfirmPassword = "Pass12345!"
            };

            var result = await controller.CompleteInvite(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
        }
    }
}
