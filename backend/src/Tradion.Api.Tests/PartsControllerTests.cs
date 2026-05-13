using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Parts;
using Xunit;

namespace Tradion.Api.Tests;

public class PartsControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Parts_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoParts()
    {
        await using var db = CreateContext();
        var controller = new PartsController(db);

        var result = await controller.List(null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PartDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenPartDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = new PartsController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenNameEmpty()
    {
        await using var db = CreateContext();
        var controller = new PartsController(db);
        var request = new CreatePartRequest { Name = "   ", Quantity = 0, ReorderLevel = 0 };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Name is required.", badRequest.Value);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WhenValid()
    {
        await using var db = CreateContext();
        var controller = new PartsController(db);
        var request = new CreatePartRequest { Name = "Part1", Description = "D", Quantity = 10, ReorderLevel = 2 };

        var result = await controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<PartDto>(created.Value);
        Assert.Equal("Part1", dto.Name);
        Assert.Equal(10, dto.Quantity);
        Assert.Equal(2, dto.ReorderLevel);
    }
}
