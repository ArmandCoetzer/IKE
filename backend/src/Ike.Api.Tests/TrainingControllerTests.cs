using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Ike.Api.DTOs.Training;
using Xunit;

namespace Ike.Api.Tests;

public class TrainingControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Training_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IWebHostEnvironment CreateMockEnv()
    {
        var mock = new Mock<IWebHostEnvironment>();
        mock.Setup(m => m.ContentRootPath).Returns(Path.GetTempPath());
        return mock.Object;
    }

    [Fact]
    public async Task ListCourses_ReturnsEmptyList_WhenNoCourses()
    {
        await using var db = CreateContext();
        var controller = new TrainingController(db, CreateMockEnv());

        var result = await controller.ListCourses();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<CourseDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetCourse_ReturnsNotFound_WhenCourseDoesNotExist()
    {
        await using var db = CreateContext();
        var controller = new TrainingController(db, CreateMockEnv());

        var result = await controller.GetCourse(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateCourse_ReturnsBadRequest_WhenNameEmpty()
    {
        await using var db = CreateContext();
        var controller = new TrainingController(db, CreateMockEnv());
        var request = new CreateCourseRequest { Name = "   ", Description = "D" };

        var result = await controller.CreateCourse(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var msg = JsonSerializer.SerializeToElement(badRequest.Value).GetProperty("message").GetString();
        Assert.Equal("Name is required.", msg);
    }

    [Fact]
    public async Task CreateCourse_ReturnsCreated_WhenNameProvided()
    {
        await using var db = CreateContext();
        var controller = new TrainingController(db, CreateMockEnv());
        var request = new CreateCourseRequest { Name = "Course1", Description = "Desc", SortOrder = 0 };

        var result = await controller.CreateCourse(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CourseDto>(created.Value);
        Assert.Equal("Course1", dto.Name);
        Assert.Equal("Desc", dto.Description);
        Assert.True(dto.IsActive);
        Assert.Equal(0, dto.ModuleCount);
    }
}
