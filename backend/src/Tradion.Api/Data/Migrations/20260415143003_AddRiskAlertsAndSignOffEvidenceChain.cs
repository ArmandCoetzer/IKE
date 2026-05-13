using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskAlertsAndSignOffEvidenceChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FinalClientSignOffEvidenceHash",
                table: "JobCards",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalClientSignOffEvidenceRecordedAt",
                table: "JobCards",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobCardSignOffEvidenceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobCardDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PreviousEvidenceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CaptureSource = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SignerDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCardSignOffEvidenceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobCardSignOffEvidenceRecords_AspNetUsers_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobCardSignOffEvidenceRecords_JobCardDocuments_JobCardDocumentId",
                        column: x => x.JobCardDocumentId,
                        principalTable: "JobCardDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobCardSignOffEvidenceRecords_JobCards_JobCardId",
                        column: x => x.JobCardId,
                        principalTable: "JobCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AlertType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FirstDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskAlerts_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskAlerts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCardSignOffEvidenceRecords_EvidenceHash",
                table: "JobCardSignOffEvidenceRecords",
                column: "EvidenceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobCardSignOffEvidenceRecords_JobCardDocumentId",
                table: "JobCardSignOffEvidenceRecords",
                column: "JobCardDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCardSignOffEvidenceRecords_JobCardId_CapturedAtUtc",
                table: "JobCardSignOffEvidenceRecords",
                columns: new[] { "JobCardId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCardSignOffEvidenceRecords_RecordedByUserId",
                table: "JobCardSignOffEvidenceRecords",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAlerts_AlertType_EntityType_EntityId_CompanyId_ResolvedAt",
                table: "RiskAlerts",
                columns: new[] { "AlertType", "EntityType", "EntityId", "CompanyId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskAlerts_CompanyId",
                table: "RiskAlerts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAlerts_ResolvedByUserId",
                table: "RiskAlerts",
                column: "ResolvedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobCardSignOffEvidenceRecords");

            migrationBuilder.DropTable(
                name: "RiskAlerts");

            migrationBuilder.DropColumn(
                name: "FinalClientSignOffEvidenceHash",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "FinalClientSignOffEvidenceRecordedAt",
                table: "JobCards");
        }
    }
}
