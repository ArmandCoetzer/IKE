using Microsoft.EntityFrameworkCore;
using Moq;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;
using Xunit;

namespace Tradion.Api.Tests;

public class ScopeGuardServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ScopeGuard_" + Guid.NewGuid())
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task CanAccessCompany_ClientScope_AllowsOwnCompany()
    {
        await using var db = CreateContext();
        var companyId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((companyId, true));
        var guard = new ScopeGuardService(db, currentUser.Object);

        var canAccess = await guard.CanAccessCompanyAsync(companyId, null, CancellationToken.None);
        Assert.True(canAccess);
    }

    [Fact]
    public async Task CanAccessJobCard_MspScope_AllowsChildCompanyJob()
    {
        await using var db = CreateContext();
        var mspId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        db.Companies.AddRange(
            new Company { Id = mspId, Name = "MSP", Type = CompanyType.Main },
            new Company { Id = clientId, Name = "Client", Type = CompanyType.Client, ParentCompanyId = mspId });
        db.Sites.Add(new Site { Id = siteId, Name = "S1", CompanyId = clientId });
        db.JobCards.Add(new JobCard { Id = jobId, JobCardNumber = "JC-1", SiteId = siteId, Status = JobCardStatus.Open, CreatedById = "u1" });
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((mspId, false));
        var guard = new ScopeGuardService(db, currentUser.Object);

        var canAccess = await guard.CanAccessJobCardAsync(jobId, CancellationToken.None);
        Assert.True(canAccess);
    }

    [Fact]
    public async Task CanAccessPermit_UsesPermitJobScope()
    {
        await using var db = CreateContext();
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var permitTypeId = Guid.NewGuid();
        var permitTemplateId = Guid.NewGuid();
        var permitId = Guid.NewGuid();
        db.Companies.Add(new Company { Id = companyId, Name = "Client", Type = CompanyType.Client });
        db.Sites.Add(new Site { Id = siteId, Name = "S1", CompanyId = companyId });
        db.JobCards.Add(new JobCard { Id = jobId, JobCardNumber = "JC-2", SiteId = siteId, Status = JobCardStatus.Open, CreatedById = "u1" });
        db.PermitTypes.Add(new PermitType { Id = permitTypeId, Name = "WA", IsWorkAuthorisation = true, IsActive = true });
        db.PermitTemplates.Add(new PermitTemplate { Id = permitTemplateId, Name = "WA Template", PermitTypeId = permitTypeId, IsActive = true });
        db.JobPermits.Add(new JobPermit { Id = permitId, JobCardId = jobId, PermitTemplateId = permitTemplateId, Status = PermitStatus.Draft, PermitNumber = 1, RequestedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetClientScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((companyId, true));
        var guard = new ScopeGuardService(db, currentUser.Object);

        var canAccess = await guard.CanAccessPermitAsync(permitId, CancellationToken.None);
        Assert.True(canAccess);
    }
}
