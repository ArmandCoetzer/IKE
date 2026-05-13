using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Controllers;
using Tradion.Api.Data;
using Tradion.Api.DTOs.JobTypes;
using Xunit;

namespace Tradion.Api.Tests;

public class JobTypesControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "JobTypes_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoJobTypes()
    {
        await using var db = CreateContext();
        var controller = new JobTypesController(db);

        var result = await controller.List(null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<JobTypeDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenJobTypeDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = new JobTypesController(db);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenNameEmpty()
    {
        await using var db = CreateContext();
        var controller = new JobTypesController(db);
        var request = new CreateJobTypeRequest { Name = "   ", Description = "D" };

        var result = await controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Name is required.", badRequest.Value);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WhenNameProvided()
    {
        await using var db = CreateContext();
        var controller = new JobTypesController(db);
        var request = new CreateJobTypeRequest { Name = "Type1", Description = "Desc" };

        var result = await controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<JobTypeDto>(created.Value);
        Assert.Equal("Type1", dto.Name);
        Assert.True(dto.IsActive);
    }
}
