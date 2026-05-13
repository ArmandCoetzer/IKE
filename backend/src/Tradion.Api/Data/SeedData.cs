using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tradion.Api.Models;
using Tradion.Api.Helpers;

namespace Tradion.Api.Data;

public static class SeedData
{
    public const string DefaultAdminConfigSection = "DefaultAdmin";
    public const string RoleAdmin = "Admin";
    public const string RoleManager = "Manager";
    public const string RoleCoordinator = "Coordinator";
    public const string RoleTechnician = "Technician";
    public const string RoleClient = "Client";

    public const string RegistrationStatusInvited = "Invited";
    public const string RegistrationStatusRegistered = "Registered";

    public static async Task EnsureSeedAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        await EnsureRolesAsync(roleManager);
        await EnsurePermissionsAndRolePermissionsAsync(db, roleManager);
        await EnsureSampleTrainingAsync(db);
    }

    public static async Task EnsureDefaultAdminAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var section = configuration.GetSection(DefaultAdminConfigSection);
        if (!section.Exists())
            return;

        var companyName = section["CompanyName"]?.Trim();
        var email = section["Email"]?.Trim();
        var password = section["Password"];
        var fullName = section["FullName"]?.Trim();
        var companyPhone = section["CompanyPhone"]?.Trim();
        var companyAddress = section["CompanyAddress"]?.Trim();

        if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger?.LogWarning("{Section} is configured but missing one of CompanyName/Email/Password. Skipping default admin bootstrap.", DefaultAdminConfigSection);
            return;
        }

        var normalizedEmail = userManager.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            logger?.LogWarning("Default admin email {Email} is invalid. Skipping bootstrap.", email);
            return;
        }

        var existingUser = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.IsActive, ct);
        if (existingUser != null)
            return;

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Name == companyName && c.Type == CompanyType.Main, ct);
        if (company == null)
        {
            company = new Company
            {
                Id = Guid.NewGuid(),
                Name = companyName,
                Type = CompanyType.Main,
                IsActive = true,
                ContactEmail = email,
                ContactPhone = string.IsNullOrWhiteSpace(companyPhone) ? null : companyPhone,
                Address = string.IsNullOrWhiteSpace(companyAddress) ? null : companyAddress
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync(ct);
        }

        var user = new ApplicationUser
        {
            UserName = UserIdentityNames.ScopedUserName(userManager, company.Id, email),
            Email = email,
            EmailConfirmed = true,
            FullName = string.IsNullOrWhiteSpace(fullName) ? email : fullName,
            CompanyId = company.Id,
            IsActive = true,
            RegistrationStatus = RegistrationStatusRegistered
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errs = string.Join(" ", result.Errors.Select(e => e.Description));
            logger?.LogWarning("Default admin bootstrap user creation failed for {Email}: {Errors}", email, errs);
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleAdmin);
        if (!roleResult.Succeeded)
        {
            var errs = string.Join(" ", roleResult.Errors.Select(e => e.Description));
            logger?.LogWarning("Default admin created, but role assignment failed for {Email}: {Errors}", email, errs);
            return;
        }

        logger?.LogInformation("Default admin bootstrap completed for {Email}.", email);
    }

    private static async Task EnsureSampleTrainingAsync(ApplicationDbContext db)
    {
        if (await db.Courses.AnyAsync())
            return;
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var quizId = Guid.NewGuid();
        var q1Id = Guid.NewGuid();
        var q2Id = Guid.NewGuid();
        db.Courses.Add(new Course
        {
            Id = courseId,
            Name = "Safety & Procedures",
            Description = "Introduction to site safety and standard procedures.",
            SortOrder = 0,
            IsActive = true
        });
        db.TrainingModules.Add(new TrainingModule
        {
            Id = moduleId,
            CourseId = courseId,
            Title = "Welcome and safety basics",
            ContentHtml = "<p>This module covers basic safety awareness and site procedures.</p><p>Complete the content and take the short quiz to earn your completion.</p>",
            VideoUrl = null,
            SortOrder = 0,
            IsActive = true
        });
        db.TrainingQuizzes.Add(new TrainingQuiz
        {
            Id = quizId,
            ModuleId = moduleId,
            Name = "Safety basics quiz",
            PassScore = 70,
            CreatedAt = DateTime.UtcNow
        });
        db.QuizQuestions.Add(new QuizQuestion
        {
            Id = q1Id,
            QuizId = quizId,
            QuestionText = "What should you do before starting work on site?",
            OptionsJson = "[\"Check in with supervisor\",\"Start immediately\",\"Skip the briefing\"]",
            CorrectIndex = 0,
            SortOrder = 0
        });
        db.QuizQuestions.Add(new QuizQuestion
        {
            Id = q2Id,
            QuizId = quizId,
            QuestionText = "Who is responsible for safety on site?",
            OptionsJson = "[\"Everyone\",\"Only the manager\",\"Only the client\"]",
            CorrectIndex = 0,
            SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var name in new[] { RoleAdmin, RoleManager, RoleCoordinator, RoleTechnician, RoleClient })
        {
            if (await roleManager.FindByNameAsync(name) == null)
                await roleManager.CreateAsync(new IdentityRole(name));
        }
    }

    private static async Task EnsurePermissionsAndRolePermissionsAsync(ApplicationDbContext db, RoleManager<IdentityRole> roleManager)
    {
        if (await db.Permissions.AnyAsync())
            return;

        var permissions = new List<Permission>
        {
            new() { Id = Guid.NewGuid(), Name = "ViewUsers", Description = "View users list" },
            new() { Id = Guid.NewGuid(), Name = "EditUsers", Description = "Create and edit users" },
            new() { Id = Guid.NewGuid(), Name = "DeleteUsers", Description = "Delete or deactivate users" },
            new() { Id = Guid.NewGuid(), Name = "ManagePermissions", Description = "Manage role and manager permissions" },
            new() { Id = Guid.NewGuid(), Name = "ManageManagerPermissions", Description = "Restrict manager permissions" },
            new() { Id = Guid.NewGuid(), Name = "ViewSites", Description = "View sites" },
            new() { Id = Guid.NewGuid(), Name = "EditSites", Description = "Create and edit sites" },
            new() { Id = Guid.NewGuid(), Name = "ViewClients", Description = "View clients" },
            new() { Id = Guid.NewGuid(), Name = "EditClients", Description = "Create and edit clients" },
            new() { Id = Guid.NewGuid(), Name = "ViewRequests", Description = "View service requests" },
            new() { Id = Guid.NewGuid(), Name = "ProcessRequests", Description = "Process and create job cards from requests" },
            new() { Id = Guid.NewGuid(), Name = "ViewJobCards", Description = "View job cards" },
            new() { Id = Guid.NewGuid(), Name = "CreateJobCards", Description = "Create and assign job cards" },
            new() { Id = Guid.NewGuid(), Name = "AssignTechnicians", Description = "Assign technicians to jobs" },
            new() { Id = Guid.NewGuid(), Name = "ViewPermits", Description = "View job permits" },
            new() { Id = Guid.NewGuid(), Name = "ApprovePermits", Description = "Approve or reject permits" },
            new() { Id = Guid.NewGuid(), Name = "ViewTraining", Description = "View training and badges" },
            new() { Id = Guid.NewGuid(), Name = "ManageTraining", Description = "Manage courses and badges" },
            new() { Id = Guid.NewGuid(), Name = "ViewReports", Description = "View reports" },
            new() { Id = Guid.NewGuid(), Name = "ManageInvoices", Description = "Create, edit, send invoices and collections" },
            new() { Id = Guid.NewGuid(), Name = "ViewPurchaseOrders", Description = "View purchase orders and parts" },
            new() { Id = Guid.NewGuid(), Name = "ManagePurchaseOrders", Description = "Manage purchase orders and parts" }
        };

        db.Permissions.AddRange(permissions);
        await db.SaveChangesAsync();

        var adminRole = await roleManager.FindByNameAsync(RoleAdmin);
        var managerRole = await roleManager.FindByNameAsync(RoleManager);
        var coordinatorRole = await roleManager.FindByNameAsync(RoleCoordinator);
        var technicianRole = await roleManager.FindByNameAsync(RoleTechnician);
        var clientRole = await roleManager.FindByNameAsync(RoleClient);

        if (adminRole == null || managerRole == null || coordinatorRole == null || technicianRole == null || clientRole == null)
            return;

        foreach (var p in permissions)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = p.Id });
            db.RolePermissions.Add(new RolePermission { RoleId = managerRole.Id, PermissionId = p.Id });
        }

        var coordinatorPerms = new[] { "ViewSites", "ViewClients", "ViewRequests", "ProcessRequests", "ViewJobCards", "CreateJobCards", "AssignTechnicians", "ViewPermits", "ViewTraining", "ViewReports", "ManageInvoices", "ViewPurchaseOrders", "ManagePurchaseOrders" };
        foreach (var name in coordinatorPerms)
        {
            var p = permissions.FirstOrDefault(x => x.Name == name);
            if (p != null)
                db.RolePermissions.Add(new RolePermission { RoleId = coordinatorRole.Id, PermissionId = p.Id });
        }

        var technicianPerms = new[] { "ViewJobCards", "ViewPermits", "ViewTraining", "ViewPurchaseOrders" };
        foreach (var name in technicianPerms)
        {
            var p = permissions.FirstOrDefault(x => x.Name == name);
            if (p != null)
                db.RolePermissions.Add(new RolePermission { RoleId = technicianRole.Id, PermissionId = p.Id });
        }

        var clientPerms = new[] { "ViewRequests", "ViewJobCards", "ViewPermits", "ApprovePermits", "ViewReports", "ViewSites", "ViewPurchaseOrders" };
        foreach (var name in clientPerms)
        {
            var p = permissions.FirstOrDefault(x => x.Name == name);
            if (p != null)
                db.RolePermissions.Add(new RolePermission { RoleId = clientRole.Id, PermissionId = p.Id });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Idempotent: adds ManageInvoices permission and grants it to Admin, Manager, Coordinator (existing databases).</summary>
    public static async Task EnsureManageInvoicesPermissionAsync(ApplicationDbContext db, RoleManager<IdentityRole> roleManager, CancellationToken ct = default)
    {
        const string permName = "ManageInvoices";
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permName, ct);
        if (perm == null)
        {
            perm = new Permission { Id = Guid.NewGuid(), Name = permName, Description = "Create, edit, send invoices and collections" };
            db.Permissions.Add(perm);
            await db.SaveChangesAsync(ct);
        }

        foreach (var roleName in new[] { RoleAdmin, RoleManager, RoleCoordinator })
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role == null) continue;
            if (await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, ct))
                continue;
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Idempotent: grants ViewPurchaseOrders to Client role so portal users can view and accept quotes scoped to their company.</summary>
    public static async Task EnsureClientQuoteViewPermissionAsync(ApplicationDbContext db, RoleManager<IdentityRole> roleManager, CancellationToken ct = default)
    {
        const string permName = "ViewPurchaseOrders";
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permName, ct);
        if (perm == null)
            return;

        var clientRole = await roleManager.FindByNameAsync(RoleClient);
        if (clientRole == null)
            return;

        if (await db.RolePermissions.AnyAsync(rp => rp.RoleId == clientRole.Id && rp.PermissionId == perm.Id, ct))
            return;

        db.RolePermissions.Add(new RolePermission { RoleId = clientRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync(ct);
    }
}
