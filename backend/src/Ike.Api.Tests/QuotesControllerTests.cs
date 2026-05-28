using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Ike.Api.DTOs.Quotes;
using Ike.Api.Models;
using Ike.Api.Services;
using Xunit;

namespace Ike.Api.Tests;

public class QuotesControllerTests
{
    private static UserManager<ApplicationUser> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new UserManager<ApplicationUser>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "Quotes_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task List_ReturnsEmptyList_WhenNoQuotes()
    {
        await using var db = CreateContext();
        var emailMock = new Mock<IEmailService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var transitionsMock = new Mock<IStatusTransitionService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        scopeGuardMock.Setup(x => x.CanAccessCompanyAsync(It.IsAny<Guid?>(), It.IsAny<Company?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var permMock = new Mock<IPermissionService>();
        var controller = new QuotesController(db, emailMock.Object, currentUserMock.Object, transitionsMock.Object, scopeGuardMock.Object, CreateUserManager(), permMock.Object);

        var result = await controller.List(null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<QuoteDto>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenQuoteDoesNotExist()
    {
        await using var db = CreateContext();
        var emailMock = new Mock<IEmailService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var transitionsMock = new Mock<IStatusTransitionService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        var permMock = new Mock<IPermissionService>();
        var controller = new QuotesController(db, emailMock.Object, currentUserMock.Object, transitionsMock.Object, scopeGuardMock.Object, CreateUserManager(), permMock.Object);

        var result = await controller.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenClientOrSiteNotFound()
    {
        await using var db = CreateContext();
        var emailMock = new Mock<IEmailService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var transitionsMock = new Mock<IStatusTransitionService>();
        var scopeGuardMock = new Mock<IScopeGuardService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        var permMock = new Mock<IPermissionService>();
        var controller = new QuotesController(db, emailMock.Object, currentUserMock.Object, transitionsMock.Object, scopeGuardMock.Object, CreateUserManager(), permMock.Object);
        var request = new CreateQuoteRequest
        {
            ClientId = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Amount = 100,
            Currency = "ZAR",
            Description = "Test"
        };

        var result = await controller.Create(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Client or site not found.", badRequest.Value);
    }

    [Fact]
    public async Task Send_AllowsAcceptedQuote_AndPreservesAcceptedStatus()
    {
        await using var db = CreateContext();
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        db.Companies.Add(new Company
        {
            Id = companyId,
            Name = "Client",
            Type = CompanyType.Client,
            ContactEmail = "client@example.com",
            IsActive = true
        });
        db.Sites.Add(new Site
        {
            Id = siteId,
            CompanyId = companyId,
            Name = "Site",
            IsActive = true
        });
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            QuoteNumber = "QUO-TEST",
            CompanyId = companyId,
            SiteId = siteId,
            Amount = 100,
            Currency = "ZAR",
            Description = "Accepted quote",
            Status = QuoteStatus.Accepted,
            CreatedById = "test-user"
        });
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(x => x.SendQuoteToClientAsync(quoteId, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((null as Guid?, false));
        var scopeGuardMock = new Mock<IScopeGuardService>();
        var permMock = new Mock<IPermissionService>();
        var controller = new QuotesController(db, emailMock.Object, currentUserMock.Object, new StatusTransitionService(), scopeGuardMock.Object, CreateUserManager(), permMock.Object);

        var result = await controller.Send(quoteId, null, true, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var quote = await db.Quotes.SingleAsync(q => q.Id == quoteId);
        Assert.Equal(QuoteStatus.Accepted, quote.Status);
        Assert.NotNull(quote.SentAt);
    }
}
