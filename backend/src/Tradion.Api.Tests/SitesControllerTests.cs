using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Sites;
using Xunit;

namespace Tradion.Api.Tests;

public class SitesControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Sites_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoSites()
    {
        await using var db = CreateContext();
        var controller = new SitesController(db);

        var result = await controller.List(null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<SiteDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenSiteDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = new SitesController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenNameEmpty()
    {
        await using var db = CreateContext();
        var controller = new SitesController(db);
        var request = new CreateSiteRequest { Name = "   ", Address = "Addr" };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Name is required.", badRequest.Value);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WhenNameProvided()
    {
        await using var db = CreateContext();
        var controller = new SitesController(db);
        var request = new CreateSiteRequest { Name = "Site1", Address = "Addr 1" };

        var result = await controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<SiteDto>(created.Value);
        Assert.Equal("Site1", dto.Name);
        Assert.Equal("Addr 1", dto.Address);
        Assert.True(dto.IsActive);
    }
}
