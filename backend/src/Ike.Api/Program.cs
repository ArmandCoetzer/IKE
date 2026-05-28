using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Ike.Api.Authorization;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Hubs;
using Ike.Api.Models;
using Ike.Api.Permits;
using Ike.Api.Services;

// QuestPDF: use free Community license (no payment required)
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
// Machine-specific secrets (SMTP password, etc.). File is gitignored; overrides appsettings*.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Frontend expects API at http://localhost:5020; 0.0.0.0 allows Flutter on same LAN (e.g. http://<your-ip>:5020)
if (builder.Environment.IsDevelopment())
    builder.WebHost.UseUrls("http://0.0.0.0:5020");

builder.Services.AddControllers(options =>
{
    options.Filters.Add<EndpointErrorAuditFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Docker, nginx, and Cloudflare Tunnel sit in front of the API with dynamic internal IPs.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase("IkeIntegration"));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Server=(localdb)\\mssqllocaldb;Database=Ike;Trusted_Connection=True;MultipleActiveResultSets=true";
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString));
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = false; // Emails may repeat across companies; UserName is scoped per org (see UserIdentityNames).
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var jwtKeyBytes = JwtKeyHelper.ResolveValidatedSigningKeyBytes(builder.Configuration, builder.Environment);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "Ike.Api",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "Ike.Web",
        IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireViewUsers", p => p.Requirements.Add(new PermissionRequirement("ViewUsers")));
    options.AddPolicy("RequireEditUsers", p => p.Requirements.Add(new PermissionRequirement("EditUsers")));
    options.AddPolicy("RequireDeleteUsers", p => p.Requirements.Add(new PermissionRequirement("DeleteUsers")));
    options.AddPolicy("RequireManagePermissions", p => p.Requirements.Add(new PermissionRequirement("ManagePermissions")));
    options.AddPolicy("RequireManageManagerPermissions", p => p.Requirements.Add(new PermissionRequirement("ManageManagerPermissions")));
    options.AddPolicy("RequireViewSites", p => p.Requirements.Add(new PermissionRequirement("ViewSites")));
    options.AddPolicy("RequireEditSites", p => p.Requirements.Add(new PermissionRequirement("EditSites")));
    options.AddPolicy("RequireViewClients", p => p.Requirements.Add(new PermissionRequirement("ViewClients")));
    options.AddPolicy("RequireEditClients", p => p.Requirements.Add(new PermissionRequirement("EditClients")));
    options.AddPolicy("RequireViewRequests", p => p.Requirements.Add(new PermissionRequirement("ViewRequests")));
    options.AddPolicy("RequireProcessRequests", p => p.Requirements.Add(new PermissionRequirement("ProcessRequests")));
    options.AddPolicy("RequireViewJobCards", p => p.Requirements.Add(new PermissionRequirement("ViewJobCards")));
    options.AddPolicy("RequireCreateJobCards", p => p.Requirements.Add(new PermissionRequirement("CreateJobCards")));
    options.AddPolicy("RequireAssignTechnicians", p => p.Requirements.Add(new PermissionRequirement("AssignTechnicians")));
    options.AddPolicy("RequireViewPermits", p => p.Requirements.Add(new PermissionRequirement("ViewPermits")));
    options.AddPolicy("RequireApprovePermits", p => p.Requirements.Add(new PermissionRequirement("ApprovePermits")));
    options.AddPolicy("RequireViewPurchaseOrders", p => p.Requirements.Add(new PermissionRequirement("ViewPurchaseOrders")));
    options.AddPolicy("RequireManagePurchaseOrders", p => p.Requirements.Add(new PermissionRequirement("ManagePurchaseOrders")));
    options.AddPolicy("RequireViewReports", p => p.Requirements.Add(new PermissionRequirement("ViewReports")));
    options.AddPolicy("RequireManageInvoices", p => p.Requirements.Add(new PermissionRequirement("ManageInvoices")));
    options.AddPolicy("RequireViewTraining", p => p.Requirements.Add(new PermissionRequirement("ViewTraining")));
    options.AddPolicy("RequireManageTraining", p => p.Requirements.Add(new PermissionRequirement("ManageTraining")));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IScopeGuardService, ScopeGuardService>();
builder.Services.AddScoped<IStatusTransitionService, StatusTransitionService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<SmtpEmailProvider>();
builder.Services.AddHttpClient<MicrosoftGraphEmailProvider>();
builder.Services.AddScoped<IEmailProvider>(sp =>
{
    var provider = sp.GetRequiredService<IConfiguration>()["EmailSettings:Provider"]?.Trim();
    if (string.Equals(provider, "MicrosoftGraph", StringComparison.OrdinalIgnoreCase))
        return sp.GetRequiredService<MicrosoftGraphEmailProvider>();
    return sp.GetRequiredService<SmtpEmailProvider>();
});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IWorkAuthorizationPermitRulesService, WorkAuthorizationPermitRulesService>();
builder.Services.AddScoped<IWorkAuthorizationDocumentRenderer, WorkAuthorizationDocumentRenderer>();
builder.Services.AddScoped<IChildPermitDocumentationPdfRenderer, ChildPermitDocumentationPdfRenderer>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<EndpointErrorAuditFilter>();
builder.Services.AddSingleton<IRealtimeHub, RealtimeHub>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<PermitExpiryNotificationHostedService>();
    builder.Services.AddHostedService<PaymentReminderHostedService>();
    builder.Services.AddHostedService<OperationalRiskAlertsHostedService>();
}

// Browsers send a preflight OPTIONS for cross-origin XHR; origin must match exactly (including port).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
var defaultDevOrigins = new[]
{
    "http://localhost:4200", "http://localhost:4201", "http://localhost:4300",
    "http://127.0.0.1:4200", "http://127.0.0.1:4201", "http://127.0.0.1:4300"
};
var allowedOrigins = corsOrigins is { Length: > 0 } ? corsOrigins : defaultDevOrigins;

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials(); // Required for SignalR with access token
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupBootstrap");
    if (!app.Environment.IsEnvironment("Testing"))
    {
        await db.Database.MigrateAsync();
        // Ensure Sites has Latitude/Longitude (idempotent - for DBs that missed the migration)
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[Sites]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Sites]') AND name = 'Latitude')
                ALTER TABLE [Sites] ADD [Latitude] float NULL;
            IF OBJECT_ID(N'[dbo].[Sites]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Sites]') AND name = 'Longitude')
                ALTER TABLE [Sites] ADD [Longitude] float NULL;
        ");
        // Ensure JobCards has ActiveJobPermitId (idempotent - for DBs that missed the migration)
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[JobCards]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JobCards]') AND name = 'ActiveJobPermitId')
                ALTER TABLE [JobCards] ADD [ActiveJobPermitId] uniqueidentifier NULL;
        ");
        // Ensure IncidentReports has Status and Resolution (idempotent - for DBs that missed the migration)
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[IncidentReports]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[IncidentReports]') AND name = 'Resolution')
                ALTER TABLE [IncidentReports] ADD [Resolution] nvarchar(2000) NULL;
            IF OBJECT_ID(N'[dbo].[IncidentReports]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[IncidentReports]') AND name = 'Status')
                ALTER TABLE [IncidentReports] ADD [Status] nvarchar(32) NOT NULL DEFAULT 'Open';
        ");
        // Ensure JobCards has BlockedReason, BlockedAt, BlockedByUserId (W-02)
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[JobCards]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JobCards]') AND name = 'BlockedReason')
                ALTER TABLE [JobCards] ADD [BlockedReason] nvarchar(500) NULL;
            IF OBJECT_ID(N'[dbo].[JobCards]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JobCards]') AND name = 'BlockedAt')
                ALTER TABLE [JobCards] ADD [BlockedAt] datetime2 NULL;
            IF OBJECT_ID(N'[dbo].[JobCards]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JobCards]') AND name = 'BlockedByUserId')
                ALTER TABLE [JobCards] ADD [BlockedByUserId] nvarchar(450) NULL;
        ");
        // Ensure AuditLogs table exists (idempotent - for DBs that missed the migration)
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AuditLogs')
            BEGIN
                CREATE TABLE [dbo].[AuditLogs] (
                    [Id] uniqueidentifier NOT NULL,
                    [Action] nvarchar(64) NOT NULL,
                    [EntityType] nvarchar(64) NOT NULL,
                    [EntityId] nvarchar(64) NULL,
                    [UserId] nvarchar(450) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [Details] nvarchar(2000) NULL,
                    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs]([CreatedAt]);
                CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs]([EntityType], [EntityId]);
            END
        ");
        // Ensure TechnicianLocations table exists (idempotent - for DBs that missed the migration)
       await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TechnicianLocations')
            BEGIN
                CREATE TABLE [dbo].[TechnicianLocations] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] nvarchar(450) NOT NULL,
                    [JobCardId] uniqueidentifier NULL,
                    [Latitude] float NOT NULL,
                    [Longitude] float NOT NULL,
                    [AccuracyMeters] float NULL,
                    [ReportedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_TechnicianLocations] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_TechnicianLocations_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_TechnicianLocations_JobCards_JobCardId] FOREIGN KEY ([JobCardId]) REFERENCES [JobCards]([Id]) ON DELETE NO ACTION
                );
                CREATE INDEX [IX_TechnicianLocations_JobCardId] ON [TechnicianLocations]([JobCardId]);
                CREATE INDEX [IX_TechnicianLocations_UserId_ReportedAt] ON [TechnicianLocations]([UserId], [ReportedAt]);
            END
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[PermitTemplates]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[PermitTemplates]') AND name = 'FormSchemaJson')
                ALTER TABLE [PermitTemplates] ADD [FormSchemaJson] nvarchar(max) NULL;
            IF OBJECT_ID(N'[dbo].[JobPermits]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JobPermits]') AND name = 'FormSnapshotJson')
                ALTER TABLE [JobPermits] ADD [FormSnapshotJson] nvarchar(max) NULL;
        ");
        // Ensure InvoiceLineItems has DiscountPercent (idempotent - for DBs with migration history drift)
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[InvoiceLineItems]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[InvoiceLineItems]') AND name = 'DiscountPercent')
                ALTER TABLE [InvoiceLineItems] ADD [DiscountPercent] decimal(5,2) NOT NULL CONSTRAINT [DF_InvoiceLineItems_DiscountPercent] DEFAULT (0);
        ");
        // Ensure quote discount columns exist (idempotent - for DBs with migration history drift)
        await db.Database.ExecuteSqlRawAsync(@"
            IF OBJECT_ID(N'[dbo].[Quotes]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') AND name = 'DiscountMode')
                ALTER TABLE [Quotes] ADD [DiscountMode] nvarchar(16) NOT NULL CONSTRAINT [DF_Quotes_DiscountMode] DEFAULT ('None');

            IF OBJECT_ID(N'[dbo].[Quotes]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Quotes]') AND name = 'GlobalDiscountPercent')
                ALTER TABLE [Quotes] ADD [GlobalDiscountPercent] decimal(5,2) NOT NULL CONSTRAINT [DF_Quotes_GlobalDiscountPercent] DEFAULT (0);

            IF OBJECT_ID(N'[dbo].[QuoteLineItems]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[QuoteLineItems]') AND name = 'DiscountPercent')
                ALTER TABLE [QuoteLineItems] ADD [DiscountPercent] decimal(5,2) NOT NULL CONSTRAINT [DF_QuoteLineItems_DiscountPercent] DEFAULT (0);
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AuditErrorEntries')
            BEGIN
                CREATE TABLE [dbo].[AuditErrorEntries] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] nvarchar(450) NULL,
                    [Method] nvarchar(16) NOT NULL,
                    [Path] nvarchar(512) NOT NULL,
                    [StatusCode] int NOT NULL,
                    [Message] nvarchar(2000) NOT NULL,
                    [Details] nvarchar(max) NULL,
                    [TraceId] nvarchar(128) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_AuditErrorEntries] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_AuditErrorEntries_CreatedAt] ON [AuditErrorEntries]([CreatedAt]);
                CREATE INDEX [IX_AuditErrorEntries_StatusCode] ON [AuditErrorEntries]([StatusCode]);
            END
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            IF EXISTS (
                SELECT 1
                FROM sys.columns c
                JOIN sys.tables t ON c.object_id = t.object_id
                WHERE t.name = 'AuditErrorEntries'
                  AND c.name = 'Details'
                  AND c.max_length <> -1
            )
            BEGIN
                ALTER TABLE [dbo].[AuditErrorEntries] ALTER COLUMN [Details] nvarchar(max) NULL;
            END
        ");
        // Cleanup previously leaked notification rows for client-role users.
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE n
            FROM [Notifications] n
            INNER JOIN [AspNetUserRoles] ur ON ur.[UserId] = n.[UserId]
            INNER JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
            WHERE r.[Name] = 'Client';
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BugLogs')
            BEGIN
                CREATE TABLE [dbo].[BugLogs] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] nvarchar(450) NULL,
                    [Title] nvarchar(256) NULL,
                    [Description] nvarchar(4000) NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_BugLogs] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_BugLogs_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers]([Id]) ON DELETE SET NULL
                );
                CREATE INDEX [IX_BugLogs_CreatedAt] ON [BugLogs]([CreatedAt]);
            END
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BugLogAttachments')
            BEGIN
                CREATE TABLE [dbo].[BugLogAttachments] (
                    [Id] uniqueidentifier NOT NULL,
                    [BugLogId] uniqueidentifier NOT NULL,
                    [FileName] nvarchar(256) NOT NULL,
                    [FilePath] nvarchar(512) NOT NULL,
                    [ContentType] nvarchar(128) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [PK_BugLogAttachments] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_BugLogAttachments_BugLogs_BugLogId] FOREIGN KEY ([BugLogId]) REFERENCES [BugLogs]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_BugLogAttachments_BugLogId] ON [BugLogAttachments]([BugLogId]);
            END
        ");
    }
    await SeedData.EnsureSeedAsync(db, userManager, roleManager);
    await SeedData.EnsureManageInvoicesPermissionAsync(db, roleManager);
    await SeedData.EnsureClientQuoteViewPermissionAsync(db, roleManager);
    await SeedData.EnsureDefaultAdminAsync(db, userManager, builder.Configuration, logger);
    if (!app.Environment.IsEnvironment("Testing"))
        await PermitCatalogSeeder.EnsureAsync(db);
}

if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
    return;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseCors();
// In Development we only listen on HTTP (5020); skip HTTPS redirect so the frontend is not sent to https://localhost:7188
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<AppHub>("/hubs/app");

app.Run();

public partial class Program { }
