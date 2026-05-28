using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Models;

namespace Ike.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ManagerPermission> ManagerPermissions => Set<ManagerPermission>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<ServiceRequestAttachment> ServiceRequestAttachments => Set<ServiceRequestAttachment>();
    public DbSet<JobCard> JobCards => Set<JobCard>();
    public DbSet<JobCardAssignment> JobCardAssignments => Set<JobCardAssignment>();
    public DbSet<JobCardDocument> JobCardDocuments => Set<JobCardDocument>();
    public DbSet<JobCardSignOffEvidence> JobCardSignOffEvidenceRecords => Set<JobCardSignOffEvidence>();
    public DbSet<JobPart> JobParts => Set<JobPart>();
    public DbSet<JobCardPlannedPart> JobCardPlannedParts => Set<JobCardPlannedPart>();
    public DbSet<PermitType> PermitTypes => Set<PermitType>();
    public DbSet<PermitTemplate> PermitTemplates => Set<PermitTemplate>();
    public DbSet<JobPermit> JobPermits => Set<JobPermit>();
    public DbSet<JobPermitMasterLink> JobPermitMasterLinks => Set<JobPermitMasterLink>();
    public DbSet<JobPermitAttachment> JobPermitAttachments => Set<JobPermitAttachment>();
    public DbSet<IncidentReport> IncidentReports => Set<IncidentReport>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RiskAlert> RiskAlerts => Set<RiskAlert>();
    public DbSet<ClientBudget> ClientBudgets => Set<ClientBudget>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<TrainingModule> TrainingModules => Set<TrainingModule>();
    public DbSet<TrainingQuiz> TrainingQuizzes => Set<TrainingQuiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();
    public DbSet<UserModuleProgress> UserModuleProgress => Set<UserModuleProgress>();
    public DbSet<UserQuizAttempt> UserQuizAttempts => Set<UserQuizAttempt>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLineItem> QuoteLineItems => Set<QuoteLineItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<PartSupplier> PartSuppliers => Set<PartSupplier>();
    public DbSet<SupplierQuoteRequest> SupplierQuoteRequests => Set<SupplierQuoteRequest>();
    public DbSet<TechnicianLocation> TechnicianLocations => Set<TechnicianLocation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditErrorEntry> AuditErrorEntries => Set<AuditErrorEntry>();
    public DbSet<BugLog> BugLogs => Set<BugLog>();
    public DbSet<BugLogAttachment> BugLogAttachments => Set<BugLogAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Permission>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(128);
            e.Property(p => p.Description).HasMaxLength(256);
        });

        builder.Entity<RolePermission>(e =>
        {
            e.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            e.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions).HasForeignKey(rp => rp.PermissionId);
            e.HasOne(rp => rp.Role).WithMany().HasForeignKey(rp => rp.RoleId);
        });

        builder.Entity<ManagerPermission>(e =>
        {
            e.HasKey(mp => new { mp.ManagerUserId, mp.PermissionId });
            e.HasOne(mp => mp.ManagerUser).WithMany(u => u.ManagerPermissions).HasForeignKey(mp => mp.ManagerUserId);
            e.HasOne(mp => mp.Permission).WithMany(p => p.ManagerPermissions).HasForeignKey(mp => mp.PermissionId);
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.HasOne(u => u.Company).WithMany(c => c.Users).HasForeignKey(u => u.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(u => u.Site).WithMany().HasForeignKey(u => u.SiteId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Company>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(256);
            e.Property(c => c.ContactEmail).HasMaxLength(256);
            e.Property(c => c.ContactPhone).HasMaxLength(50);
            e.Property(c => c.Address).HasMaxLength(500);
            e.HasOne(c => c.ParentCompany).WithMany(c => c.ChildCompanies).HasForeignKey(c => c.ParentCompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Site>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(256);
            e.Property(s => s.Address).HasMaxLength(500);
            e.HasOne(s => s.Company).WithMany(c => c.Sites).HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TechnicianLocation>(e =>
        {
            e.HasKey(tl => tl.Id);
            e.HasOne(tl => tl.User).WithMany().HasForeignKey(tl => tl.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(tl => tl.JobCard).WithMany().HasForeignKey(tl => tl.JobCardId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(tl => new { tl.UserId, tl.ReportedAt });
        });

        builder.Entity<ServiceRequest>(e =>
        {
            e.HasKey(sr => sr.Id);
            e.Property(sr => sr.RequestNumber).HasMaxLength(32);
            e.Property(sr => sr.Description).HasMaxLength(2000);
            e.Property(sr => sr.Status).HasMaxLength(32);
            e.Property(sr => sr.PenaltyFee).HasPrecision(18, 2);
            e.HasOne(sr => sr.Site).WithMany().HasForeignKey(sr => sr.SiteId);
            e.HasOne(sr => sr.RequestedByUser).WithMany().HasForeignKey(sr => sr.RequestedByUserId);
        });

        builder.Entity<ServiceRequestAttachment>(e =>
        {
            e.HasKey(sra => sra.Id);
            e.Property(sra => sra.FileName).HasMaxLength(256);
            e.Property(sra => sra.FilePath).HasMaxLength(512);
            e.Property(sra => sra.ContentType).HasMaxLength(128);
            e.HasOne(sra => sra.ServiceRequest).WithMany(sr => sr.Attachments).HasForeignKey(sra => sra.ServiceRequestId);
        });

        builder.Entity<JobCard>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.JobCardNumber).HasMaxLength(32);
            e.Property(j => j.Status).HasMaxLength(32);
            e.Property(j => j.PaperModeActivatedByUserId).HasMaxLength(450);
            e.Property(j => j.FinalClientSignOffFileSha256).HasMaxLength(64);
            e.Property(j => j.FinalClientSignOffCaptureSource).HasMaxLength(64);
            e.Property(j => j.FinalClientSignOffEvidenceHash).HasMaxLength(64);
            e.HasOne(j => j.ServiceRequest).WithMany().HasForeignKey(j => j.ServiceRequestId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasOne(j => j.Site).WithMany().HasForeignKey(j => j.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(j => j.CreatedByUser).WithMany().HasForeignKey(j => j.CreatedById);
            e.HasOne(j => j.RequiredPermitType).WithMany().HasForeignKey(j => j.RequiredPermitTypeId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            e.HasOne(j => j.ActiveJobPermit).WithMany().HasForeignKey(j => j.ActiveJobPermitId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        });

        builder.Entity<JobCardPlannedPart>(e =>
        {
            e.HasKey(jpp => jpp.Id);
            e.HasOne(jpp => jpp.JobCard).WithMany(j => j.PlannedParts).HasForeignKey(jpp => jpp.JobCardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(jpp => jpp.Part).WithMany().HasForeignKey(jpp => jpp.PartId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JobCardAssignment>(e =>
        {
            e.HasKey(jca => new { jca.JobCardId, jca.UserId });
            e.HasOne(jca => jca.JobCard).WithMany(j => j.Assignments).HasForeignKey(jca => jca.JobCardId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(jca => jca.User).WithMany().HasForeignKey(jca => jca.UserId);
        });

        builder.Entity<JobCardDocument>(e =>
        {
            e.HasKey(jcd => jcd.Id);
            e.Property(jcd => jcd.DocumentType).HasMaxLength(64);
            e.Property(jcd => jcd.FilePath).HasMaxLength(512);
            e.HasOne(jcd => jcd.JobCard).WithMany(j => j.Documents).HasForeignKey(jcd => jcd.JobCardId);
            e.HasOne(jcd => jcd.SignedByUser).WithMany().HasForeignKey(jcd => jcd.SignedByUserId);
            e.HasOne(jcd => jcd.PurchaseOrder).WithMany(po => po.JobCardDocuments).HasForeignKey(jcd => jcd.PurchaseOrderId);
        });

        builder.Entity<JobCardSignOffEvidence>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileSha256).HasMaxLength(64);
            e.Property(x => x.EvidenceHash).HasMaxLength(64);
            e.Property(x => x.PreviousEvidenceHash).HasMaxLength(64);
            e.Property(x => x.CaptureSource).HasMaxLength(64);
            e.Property(x => x.DeviceId).HasMaxLength(128);
            e.Property(x => x.AppVersion).HasMaxLength(64);
            e.Property(x => x.SignerDisplayName).HasMaxLength(256);
            e.Property(x => x.RecordedByUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.JobCardId, x.CapturedAtUtc });
            e.HasIndex(x => x.EvidenceHash).IsUnique();
            e.HasOne(x => x.JobCard).WithMany(j => j.SignOffEvidenceRecords).HasForeignKey(x => x.JobCardId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JobCardDocument).WithMany().HasForeignKey(x => x.JobCardDocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JobPart>(e =>
        {
            e.HasKey(jp => jp.Id);
            e.Property(jp => jp.Brand).HasMaxLength(128);
            e.Property(jp => jp.SerialNumber).HasMaxLength(128);
            e.Property(jp => jp.Description).HasMaxLength(500);
            e.Property(jp => jp.OldPartPhotoPath).HasMaxLength(512);
            e.Property(jp => jp.NewPartPhotoPath).HasMaxLength(512);
            e.HasOne(jp => jp.JobCard).WithMany(j => j.Parts).HasForeignKey(jp => jp.JobCardId);
            e.HasOne(jp => jp.CreatedByUser).WithMany().HasForeignKey(jp => jp.CreatedByUserId);
        });

        builder.Entity<PermitType>(e =>
        {
            e.HasKey(pt => pt.Id);
            e.Property(pt => pt.Name).HasMaxLength(128);
            e.Property(pt => pt.Description).HasMaxLength(500);
            e.Property(pt => pt.TriggersPermitTypeIdsJson).HasMaxLength(1000);
            e.HasOne(pt => pt.Company).WithMany().HasForeignKey(pt => pt.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PermitTemplate>(e =>
        {
            e.HasKey(pt => pt.Id);
            e.Property(pt => pt.Name).HasMaxLength(256);
            e.HasOne(pt => pt.PermitType).WithMany(p => p.Templates).HasForeignKey(pt => pt.PermitTypeId);
            e.HasOne(pt => pt.Site).WithMany().HasForeignKey(pt => pt.SiteId);
            e.HasOne(pt => pt.Company).WithMany().HasForeignKey(pt => pt.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JobPermitAttachment>(e =>
        {
            e.HasKey(jpa => jpa.Id);
            e.Property(jpa => jpa.FileName).HasMaxLength(256);
            e.Property(jpa => jpa.FilePath).HasMaxLength(512);
            e.HasOne(jpa => jpa.JobPermit).WithMany(jp => jp.Attachments).HasForeignKey(jpa => jpa.JobPermitId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(jpa => jpa.UploadedByUser).WithMany().HasForeignKey(jpa => jpa.UploadedByUserId);
        });

        builder.Entity<JobPermit>(e =>
        {
            e.HasKey(jp => jp.Id);
            e.Property(jp => jp.Status).HasMaxLength(32);
            e.Property(jp => jp.WaSignedBusinessContentHash).HasMaxLength(64);
            e.Property(jp => jp.PaperPermitNumber).HasMaxLength(50);
            e.Property(jp => jp.PaperClientSignedOffByUserId).HasMaxLength(450);
            e.HasOne(jp => jp.JobCard).WithMany(j => j.Permits).HasForeignKey(jp => jp.JobCardId);
            e.HasOne(jp => jp.PermitTemplate).WithMany(pt => pt.JobPermits).HasForeignKey(jp => jp.PermitTemplateId);
            e.HasOne(jp => jp.RequestedByUser).WithMany().HasForeignKey(jp => jp.RequestedByUserId);
            e.HasOne(jp => jp.ApprovedByUser).WithMany().HasForeignKey(jp => jp.ApprovedByUserId);
            e.HasOne(jp => jp.MasterPermit).WithMany(jp => jp.ChildPermits).HasForeignKey(jp => jp.MasterPermitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JobPermitMasterLink>(e =>
        {
            e.HasKey(x => new { x.MasterPermitId, x.ChildPermitId });
            e.HasOne(x => x.MasterPermit)
                .WithMany(p => p.MasterLinks)
                .HasForeignKey(x => x.MasterPermitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ChildPermit)
                .WithMany(p => p.ChildLinks)
                .HasForeignKey(x => x.ChildPermitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ChildPermitId);
        });

        builder.Entity<IncidentReport>(e =>
        {
            e.HasKey(ir => ir.Id);
            e.Property(ir => ir.Description).HasMaxLength(2000);
            e.Property(ir => ir.Severity).HasMaxLength(32);
            e.Property(ir => ir.PhotosJson).HasMaxLength(4000);
            e.Property(ir => ir.Status).HasMaxLength(32);
            e.Property(ir => ir.Resolution).HasMaxLength(2000);
            e.HasOne(ir => ir.JobCard).WithMany(j => j.IncidentReports).HasForeignKey(ir => ir.JobCardId);
            e.HasOne(ir => ir.ReportedByUser).WithMany().HasForeignKey(ir => ir.ReportedByUserId);
        });

        builder.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).HasMaxLength(256);
            e.Property(n => n.Body).HasMaxLength(2000);
            e.Property(n => n.Type).HasMaxLength(64);
            e.Property(n => n.RelatedEntityId).HasMaxLength(128);
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId);
        });

        builder.Entity<RiskAlert>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AlertType).HasMaxLength(64);
            e.Property(x => x.Severity).HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.Details).HasMaxLength(2000);
            e.Property(x => x.EntityType).HasMaxLength(64);
            e.Property(x => x.EntityId).HasMaxLength(128);
            e.Property(x => x.ResolvedByUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.AlertType, x.EntityType, x.EntityId, x.CompanyId, x.ResolvedAt });
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ResolvedByUser).WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClientBudget>(e =>
        {
            e.HasKey(cb => cb.Id);
            e.Property(cb => cb.Currency).HasMaxLength(8);
            e.Property(cb => cb.ThresholdAmount).HasPrecision(18, 2);
            e.Property(cb => cb.SpentAmount).HasPrecision(18, 2);
            e.HasOne(cb => cb.Company).WithMany().HasForeignKey(cb => cb.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(cb => cb.ContinuationApprovedByUserId);
        });

        builder.Entity<Course>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(256);
            e.Property(c => c.Description).HasMaxLength(1000);
        });

        builder.Entity<TrainingModule>(e =>
        {
            e.HasKey(tm => tm.Id);
            e.Property(tm => tm.Title).HasMaxLength(256);
            e.Property(tm => tm.VideoUrl).HasMaxLength(512);
            e.Property(tm => tm.ContentHtml).HasMaxLength(8000);
            e.HasOne(tm => tm.Course).WithMany(c => c.Modules).HasForeignKey(tm => tm.CourseId);
        });

        builder.Entity<TrainingQuiz>(e =>
        {
            e.HasKey(tq => tq.Id);
            e.Property(tq => tq.Name).HasMaxLength(256);
            e.HasOne(tq => tq.Module).WithOne(m => m.Quiz).HasForeignKey<TrainingQuiz>(tq => tq.ModuleId);
        });

        builder.Entity<QuizQuestion>(e =>
        {
            e.HasKey(qq => qq.Id);
            e.Property(qq => qq.QuestionText).HasMaxLength(1000);
            e.Property(qq => qq.OptionsJson).HasMaxLength(2000);
            e.HasOne(qq => qq.Quiz).WithMany(q => q.Questions).HasForeignKey(qq => qq.QuizId);
        });

        builder.Entity<Badge>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).HasMaxLength(128);
            e.Property(b => b.Description).HasMaxLength(500);
            e.HasOne(b => b.Course).WithMany(c => c.Badges).HasForeignKey(b => b.CourseId);
        });

        builder.Entity<UserBadge>(e =>
        {
            e.HasKey(ub => ub.Id);
            e.HasOne(ub => ub.User).WithMany().HasForeignKey(ub => ub.UserId);
            e.HasOne(ub => ub.Badge).WithMany(b => b.UserBadges).HasForeignKey(ub => ub.BadgeId);
        });

        builder.Entity<UserModuleProgress>(e =>
        {
            e.HasKey(ump => new { ump.UserId, ump.ModuleId });
            e.HasOne(ump => ump.User).WithMany().HasForeignKey(ump => ump.UserId);
            e.HasOne(ump => ump.Module).WithMany(m => m.UserProgress).HasForeignKey(ump => ump.ModuleId);
        });

        builder.Entity<UserQuizAttempt>(e =>
        {
            e.HasKey(uqa => uqa.Id);
            e.Property(uqa => uqa.AnswersJson).HasMaxLength(4000);
            e.HasOne(uqa => uqa.User).WithMany().HasForeignKey(uqa => uqa.UserId);
            e.HasOne(uqa => uqa.Quiz).WithMany(q => q.UserAttempts).HasForeignKey(uqa => uqa.QuizId);
        });

        builder.Entity<QuoteLineItem>(e =>
        {
            e.HasKey(qli => qli.Id);
            e.Property(qli => qli.LineType).HasMaxLength(16);
            e.Property(qli => qli.Description).HasMaxLength(500);
            e.Property(qli => qli.Quantity).HasPrecision(18, 4);
            e.Property(qli => qli.UnitPrice).HasPrecision(18, 2);
            e.Property(qli => qli.DiscountPercent).HasPrecision(5, 2);
            e.HasOne(qli => qli.Quote).WithMany(q => q.LineItems).HasForeignKey(qli => qli.QuoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(qli => qli.Part).WithMany().HasForeignKey(qli => qli.PartId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InvoiceLineItem>(e =>
        {
            e.HasKey(ili => ili.Id);
            e.Property(ili => ili.LineType).HasMaxLength(16);
            e.Property(ili => ili.Description).HasMaxLength(500);
            e.Property(ili => ili.Quantity).HasPrecision(18, 4);
            e.Property(ili => ili.UnitPrice).HasPrecision(18, 2);
            e.Property(ili => ili.DiscountPercent).HasPrecision(5, 2);
            e.HasOne(ili => ili.Invoice).WithMany(i => i.LineItems).HasForeignKey(ili => ili.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ili => ili.Part).WithMany().HasForeignKey(ili => ili.PartId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Invoice>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.InvoiceNumber).HasMaxLength(32);
            e.Property(i => i.Amount).HasPrecision(18, 2);
            e.Property(i => i.Currency).HasMaxLength(8);
            e.Property(i => i.Status).HasMaxLength(32);
            e.Property(i => i.Notes).HasMaxLength(2000);
            e.Property(i => i.ReminderStage).HasDefaultValue(0);
            e.HasOne(i => i.JobCard).WithMany().HasForeignKey(i => i.JobCardId);
            e.HasOne(i => i.Quote).WithMany().HasForeignKey(i => i.QuoteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.Company).WithMany().HasForeignKey(i => i.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Site).WithMany().HasForeignKey(i => i.SiteId);
        });

        builder.Entity<Quote>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.QuoteNumber).HasMaxLength(32);
            e.Property(q => q.Amount).HasPrecision(18, 2);
            e.Property(q => q.Currency).HasMaxLength(8);
            e.Property(q => q.Description).HasMaxLength(2000);
            e.Property(q => q.Notes).HasMaxLength(2000);
            e.Property(q => q.Status).HasMaxLength(32);
            e.Property(q => q.UploadedFilePath).HasMaxLength(512);
            e.Property(q => q.UploadedFileName).HasMaxLength(256);
            e.Property(q => q.UploadedContentType).HasMaxLength(128);
            e.Property(q => q.ExtractedQuoteNumber).HasMaxLength(128);
            e.Property(q => q.ExtractedSupplierName).HasMaxLength(256);
            e.Property(q => q.DiscountMode).HasMaxLength(16);
            e.Property(q => q.GlobalDiscountPercent).HasPrecision(5, 2);
            e.HasOne(q => q.Company).WithMany().HasForeignKey(q => q.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(q => q.Site).WithMany().HasForeignKey(q => q.SiteId);
            e.HasOne(q => q.CreatedByUser).WithMany().HasForeignKey(q => q.CreatedById);
        });

        builder.Entity<PurchaseOrder>(e =>
        {
            e.HasKey(po => po.Id);
            e.Property(po => po.Amount).HasPrecision(18, 2);
            e.Property(po => po.PONumber).HasMaxLength(32);
            e.Property(po => po.ClientPONumber).HasMaxLength(64);
            e.Property(po => po.ClientPOFilePath).HasMaxLength(512);
            e.Property(po => po.Currency).HasMaxLength(8);
            e.Property(po => po.Status).HasMaxLength(32);
            e.Property(po => po.Notes).HasMaxLength(2000);
            e.HasOne(po => po.Company).WithMany().HasForeignKey(po => po.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(po => po.Site).WithMany().HasForeignKey(po => po.SiteId);
            e.HasOne(po => po.CreatedByUser).WithMany().HasForeignKey(po => po.CreatedById);
            e.HasOne(po => po.Quote).WithMany().HasForeignKey(po => po.QuoteId);
        });

        builder.Entity<Supplier>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(256);
            e.Property(s => s.Email).HasMaxLength(256);
            e.Property(s => s.WebsiteUrl).HasMaxLength(512);
            e.Property(s => s.Phone).HasMaxLength(50);
            e.Property(s => s.ContactPerson).HasMaxLength(200);
            e.HasOne(s => s.Company).WithMany().HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Part>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(256);
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.PartNumber).HasMaxLength(64);
            e.Property(p => p.UnitPrice).HasPrecision(18, 2);
            e.Property(p => p.Unit).HasMaxLength(32);
            e.HasOne(p => p.Company).WithMany().HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PartSupplier>(e =>
        {
            e.HasKey(ps => new { ps.PartId, ps.SupplierId });
            e.HasOne(ps => ps.Part).WithMany(p => p.Suppliers).HasForeignKey(ps => ps.PartId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ps => ps.Supplier).WithMany(s => s.Parts).HasForeignKey(ps => ps.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SupplierQuoteRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Part).WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.JobCard).WithMany().HasForeignKey(x => x.JobCardId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Action).HasMaxLength(64);
            e.Property(a => a.EntityType).HasMaxLength(64);
            e.Property(a => a.EntityId).HasMaxLength(64);
            e.Property(a => a.UserId).HasMaxLength(450);
            e.Property(a => a.Details).HasMaxLength(2000);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
        });

        builder.Entity<AuditErrorEntry>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Method).HasMaxLength(16);
            e.Property(a => a.Path).HasMaxLength(512);
            e.Property(a => a.Message).HasMaxLength(2000);
            e.Property(a => a.Details).HasColumnType("nvarchar(max)");
            e.Property(a => a.TraceId).HasMaxLength(128);
            e.Property(a => a.UserId).HasMaxLength(450);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => a.StatusCode);
        });

        builder.Entity<BugLog>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Title).HasMaxLength(256);
            e.Property(b => b.Description).HasMaxLength(4000);
            e.Property(b => b.UserId).HasMaxLength(450);
            e.HasOne(b => b.User).WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(b => b.CreatedAt);
        });

        builder.Entity<BugLogAttachment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.FileName).HasMaxLength(256);
            e.Property(a => a.FilePath).HasMaxLength(512);
            e.Property(a => a.ContentType).HasMaxLength(128);
            e.HasOne(a => a.BugLog).WithMany(b => b.Attachments).HasForeignKey(a => a.BugLogId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.BugLogId);
        });
    }
}
