using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderIdToJobCardDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Badges_Courses_CourseId",
                table: "Badges");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientBudgets_Clients_ClientId",
                table: "ClientBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentAttachments_Equipment_EquipmentId",
                table: "EquipmentAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_IncidentReports_JobCards_JobCardId",
                table: "IncidentReports");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCardDocuments_JobCards_JobCardId",
                table: "JobCardDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobParts_JobCards_JobCardId",
                table: "JobParts");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPermits_JobCards_JobCardId",
                table: "JobPermits");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPermits_PermitTemplates_PermitTemplateId",
                table: "JobPermits");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitTemplates_Clients_ClientId",
                table: "PermitTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitTemplates_Sites_SiteId",
                table: "PermitTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_QuizQuestions_TrainingQuizzes_QuizId",
                table: "QuizQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingModules_Courses_CourseId",
                table: "TrainingModules");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingQuizzes_TrainingModules_ModuleId",
                table: "TrainingQuizzes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserBadges_Badges_BadgeId",
                table: "UserBadges");

            migrationBuilder.DropForeignKey(
                name: "FK_UserModuleProgress_TrainingModules_ModuleId",
                table: "UserModuleProgress");

            migrationBuilder.DropForeignKey(
                name: "FK_UserQuizAttempts_TrainingQuizzes_QuizId",
                table: "UserQuizAttempts");

            migrationBuilder.AddColumn<DateTime>(
                name: "OptionalDueDate",
                table: "ServiceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestNumber",
                table: "ServiceRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "JobCardDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Clients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Quotes_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Quotes_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PONumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ClientPONumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCardDocuments_PurchaseOrderId",
                table: "JobCardDocuments",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ClientId",
                table: "PurchaseOrders",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CreatedById",
                table: "PurchaseOrders",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_QuoteId",
                table: "PurchaseOrders",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SiteId",
                table: "PurchaseOrders",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ClientId",
                table: "Quotes",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CreatedById",
                table: "Quotes",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_SiteId",
                table: "Quotes",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Badges_Courses_CourseId",
                table: "Badges",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientBudgets_Clients_ClientId",
                table: "ClientBudgets",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentAttachments_Equipment_EquipmentId",
                table: "EquipmentAttachments",
                column: "EquipmentId",
                principalTable: "Equipment",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentReports_JobCards_JobCardId",
                table: "IncidentReports",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardDocuments_JobCards_JobCardId",
                table: "JobCardDocuments",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardDocuments_PurchaseOrders_PurchaseOrderId",
                table: "JobCardDocuments",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards",
                column: "ServiceRequestId",
                principalTable: "ServiceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobParts_JobCards_JobCardId",
                table: "JobParts",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPermits_JobCards_JobCardId",
                table: "JobPermits",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPermits_PermitTemplates_PermitTemplateId",
                table: "JobPermits",
                column: "PermitTemplateId",
                principalTable: "PermitTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTemplates_Clients_ClientId",
                table: "PermitTemplates",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTemplates_Sites_SiteId",
                table: "PermitTemplates",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuizQuestions_TrainingQuizzes_QuizId",
                table: "QuizQuestions",
                column: "QuizId",
                principalTable: "TrainingQuizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingModules_Courses_CourseId",
                table: "TrainingModules",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingQuizzes_TrainingModules_ModuleId",
                table: "TrainingQuizzes",
                column: "ModuleId",
                principalTable: "TrainingModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserBadges_Badges_BadgeId",
                table: "UserBadges",
                column: "BadgeId",
                principalTable: "Badges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserModuleProgress_TrainingModules_ModuleId",
                table: "UserModuleProgress",
                column: "ModuleId",
                principalTable: "TrainingModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserQuizAttempts_TrainingQuizzes_QuizId",
                table: "UserQuizAttempts",
                column: "QuizId",
                principalTable: "TrainingQuizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Badges_Courses_CourseId",
                table: "Badges");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientBudgets_Clients_ClientId",
                table: "ClientBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentAttachments_Equipment_EquipmentId",
                table: "EquipmentAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_IncidentReports_JobCards_JobCardId",
                table: "IncidentReports");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCardDocuments_JobCards_JobCardId",
                table: "JobCardDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCardDocuments_PurchaseOrders_PurchaseOrderId",
                table: "JobCardDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobParts_JobCards_JobCardId",
                table: "JobParts");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPermits_JobCards_JobCardId",
                table: "JobPermits");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPermits_PermitTemplates_PermitTemplateId",
                table: "JobPermits");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitTemplates_Clients_ClientId",
                table: "PermitTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitTemplates_Sites_SiteId",
                table: "PermitTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_QuizQuestions_TrainingQuizzes_QuizId",
                table: "QuizQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingModules_Courses_CourseId",
                table: "TrainingModules");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingQuizzes_TrainingModules_ModuleId",
                table: "TrainingQuizzes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserBadges_Badges_BadgeId",
                table: "UserBadges");

            migrationBuilder.DropForeignKey(
                name: "FK_UserModuleProgress_TrainingModules_ModuleId",
                table: "UserModuleProgress");

            migrationBuilder.DropForeignKey(
                name: "FK_UserQuizAttempts_TrainingQuizzes_QuizId",
                table: "UserQuizAttempts");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_JobCardDocuments_PurchaseOrderId",
                table: "JobCardDocuments");

            migrationBuilder.DropColumn(
                name: "OptionalDueDate",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RequestNumber",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "JobCardDocuments");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Clients");

            migrationBuilder.AddForeignKey(
                name: "FK_Badges_Courses_CourseId",
                table: "Badges",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientBudgets_Clients_ClientId",
                table: "ClientBudgets",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentAttachments_Equipment_EquipmentId",
                table: "EquipmentAttachments",
                column: "EquipmentId",
                principalTable: "Equipment",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentReports_JobCards_JobCardId",
                table: "IncidentReports",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardDocuments_JobCards_JobCardId",
                table: "JobCardDocuments",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards",
                column: "ServiceRequestId",
                principalTable: "ServiceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobParts_JobCards_JobCardId",
                table: "JobParts",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPermits_JobCards_JobCardId",
                table: "JobPermits",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPermits_PermitTemplates_PermitTemplateId",
                table: "JobPermits",
                column: "PermitTemplateId",
                principalTable: "PermitTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTemplates_Clients_ClientId",
                table: "PermitTemplates",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTemplates_Sites_SiteId",
                table: "PermitTemplates",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuizQuestions_TrainingQuizzes_QuizId",
                table: "QuizQuestions",
                column: "QuizId",
                principalTable: "TrainingQuizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingModules_Courses_CourseId",
                table: "TrainingModules",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingQuizzes_TrainingModules_ModuleId",
                table: "TrainingQuizzes",
                column: "ModuleId",
                principalTable: "TrainingModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserBadges_Badges_BadgeId",
                table: "UserBadges",
                column: "BadgeId",
                principalTable: "Badges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserModuleProgress_TrainingModules_ModuleId",
                table: "UserModuleProgress",
                column: "ModuleId",
                principalTable: "TrainingModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserQuizAttempts_TrainingQuizzes_QuizId",
                table: "UserQuizAttempts",
                column: "QuizId",
                principalTable: "TrainingQuizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
