using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEquipmentAndSiteEquipmentRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE mp FROM ManagerPermissions mp
                INNER JOIN Permissions p ON mp.PermissionId = p.Id
                WHERE p.Name IN (N'ViewEquipment', N'EditEquipment');
                DELETE rp FROM RolePermissions rp
                INNER JOIN Permissions p ON rp.PermissionId = p.Id
                WHERE p.Name IN (N'ViewEquipment', N'EditEquipment');
                DELETE FROM Permissions WHERE Name IN (N'ViewEquipment', N'EditEquipment');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_Equipment_EquipmentId",
                table: "ServiceRequests");

            migrationBuilder.DropTable(
                name: "EquipmentAttachments");

            migrationBuilder.DropTable(
                name: "JobCardEquipment");

            migrationBuilder.DropTable(
                name: "Equipment");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_EquipmentId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "EquipmentId",
                table: "ServiceRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EquipmentId",
                table: "ServiceRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredPermitTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EquipmentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipment_PermitTypes_RequiredPermitTypeId",
                        column: x => x.RequiredPermitTypeId,
                        principalTable: "PermitTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Equipment_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentAttachments_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobCardEquipment",
                columns: table => new
                {
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCardEquipment", x => new { x.JobCardId, x.EquipmentId });
                    table.ForeignKey(
                        name: "FK_JobCardEquipment_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobCardEquipment_JobCards_JobCardId",
                        column: x => x.JobCardId,
                        principalTable: "JobCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_EquipmentId",
                table: "ServiceRequests",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_RequiredPermitTypeId",
                table: "Equipment",
                column: "RequiredPermitTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_SiteId",
                table: "Equipment",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAttachments_EquipmentId",
                table: "EquipmentAttachments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCardEquipment_EquipmentId",
                table: "JobCardEquipment",
                column: "EquipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_Equipment_EquipmentId",
                table: "ServiceRequests",
                column: "EquipmentId",
                principalTable: "Equipment",
                principalColumn: "Id");
        }
    }
}
