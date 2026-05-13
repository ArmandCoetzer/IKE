using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Clients;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class ClientsControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Clients_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mock = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(x => x.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        mock.Setup(x => x.Users).Returns(new List<ApplicationUser>().AsQueryable());
        mock.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(Array.Empty<string>());
        return mock;
    }

    private static (Mock<IEmailService>, IConfiguration) CreateEmailAndConfig()
    {
        var emailMock = new Mock<IEmailService>();
        var config = new ConfigurationBuilder().Build();
        return (emailMock, config);
    }

    private static ClientsController CreateController(ApplicationDbContext db, Mock<UserManager<ApplicationUser>>? userManager = null)
    {
        var um = userManager ?? CreateMockUserManager();
        var (emailMock, config) = CreateEmailAndConfig();
        var controller = new ClientsController(db, um.Object, emailMock.Object, config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoClients()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.List(null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<ClientDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenClientDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenCompanyNameEmpty()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);
        var request = new CreateClientRequest { CompanyName = "   " };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Company name is required.", badRequest.Value);
    }
}
