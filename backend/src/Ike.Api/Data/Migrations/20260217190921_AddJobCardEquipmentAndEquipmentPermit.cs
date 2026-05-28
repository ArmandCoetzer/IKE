using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCardEquipmentAndEquipmentPermit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PenaltyFee",
                table: "ServiceRequests",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PenaltyNote",
                table: "ServiceRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeferPricing",
                table: "Quotes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "JobCards",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "JobCards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPermitManager",
                table: "JobCardAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequiredPermitTypeId",
                table: "Equipment",
                type: "uniqueidentifier",
                nullable: true);

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
                name: "IX_Equipment_RequiredPermitTypeId",
                table: "Equipment",
                column: "RequiredPermitTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCardEquipment_EquipmentId",
                table: "JobCardEquipment",
                column: "EquipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Equipment_PermitTypes_RequiredPermitTypeId",
                table: "Equipment",
                column: "RequiredPermitTypeId",
                principalTable: "PermitTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Equipment_PermitTypes_RequiredPermitTypeId",
                table: "Equipment");

            migrationBuilder.DropTable(
                name: "JobCardEquipment");

            migrationBuilder.DropIndex(
                name: "IX_Equipment_RequiredPermitTypeId",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "PenaltyFee",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "PenaltyNote",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "DeferPricing",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "IsPermitManager",
                table: "JobCardAssignments");

            migrationBuilder.DropColumn(
                name: "RequiredPermitTypeId",
                table: "Equipment");
        }
    }
}
