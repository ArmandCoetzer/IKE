using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Ike.Api.DTOs.PermitTypes;
using Xunit;

namespace Ike.Api.Tests;

public class PermitTypesControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "PermitTypes_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoPermitTypes()
    {
        await using var db = CreateContext();
        var controller = new PermitTypesController(db);

        var result = await controller.List(null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PermitTypeDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenPermitTypeDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = new PermitTypesController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenNameEmpty()
    {
        await using var db = CreateContext();
        var controller = new PermitTypesController(db);
        var request = new CreatePermitTypeRequest { Name = "   " };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Name is required.", badRequest.Value);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WhenNameProvided()
    {
        await using var db = CreateContext();
        var controller = new PermitTypesController(db);
        var request = new CreatePermitTypeRequest { Name = "Permit1", Description = "D" };

        var result = await controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<PermitTypeDto>(created.Value);
        Assert.Equal("Permit1", dto.Name);
        Assert.True(dto.IsActive);
    }
}
