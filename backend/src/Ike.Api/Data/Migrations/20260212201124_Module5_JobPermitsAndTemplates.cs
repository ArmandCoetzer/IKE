using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Module5_JobPermitsAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards");

            migrationBuilder.CreateTable(
                name: "PermitTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PermitTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChecklistJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidityRulesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitTemplates_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitTemplates_PermitTypes_PermitTypeId",
                        column: x => x.PermitTypeId,
                        principalTable: "PermitTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PermitTemplates_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobPermits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChecklistSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPermits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobPermits_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JobPermits_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JobPermits_JobCards_JobCardId",
                        column: x => x.JobCardId,
                        principalTable: "JobCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobPermits_PermitTemplates_PermitTemplateId",
                        column: x => x.PermitTemplateId,
                        principalTable: "PermitTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPermits_ApprovedByUserId",
                table: "JobPermits",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPermits_JobCardId",
                table: "JobPermits",
                column: "JobCardId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPermits_PermitTemplateId",
                table: "JobPermits",
                column: "PermitTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPermits_RequestedByUserId",
                table: "JobPermits",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTemplates_ClientId",
                table: "PermitTemplates",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTemplates_PermitTypeId",
                table: "PermitTemplates",
                column: "PermitTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTemplates_SiteId",
                table: "PermitTemplates",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards");

            migrationBuilder.DropTable(
                name: "JobPermits");

            migrationBuilder.DropTable(
                name: "PermitTemplates");

            migrationBuilder.DropTable(
                name: "PermitTypes");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCardAssignments_JobCards_JobCardId",
                table: "JobCardAssignments",
                column: "JobCardId",
                principalTable: "JobCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards",
                column: "ServiceRequestId",
                principalTable: "ServiceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
