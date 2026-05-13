using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitsAndPartsToJobCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PartsRequired",
                table: "JobCards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PermitsRequired",
                table: "JobCards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequiredPermitTypeId",
                table: "JobCards",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobCardPlannedParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCardPlannedParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobCardPlannedParts_JobCards_JobCardId",
                        column: x => x.JobCardId,
                        principalTable: "JobCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobCardPlannedParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_RequiredPermitTypeId",
                table: "JobCards",
                column: "RequiredPermitTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCardPlannedParts_JobCardId",
                table: "JobCardPlannedParts",
                column: "JobCardId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCardPlannedParts_PartId",
                table: "JobCardPlannedParts",
                column: "PartId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_PermitTypes_RequiredPermitTypeId",
                table: "JobCards",
                column: "RequiredPermitTypeId",
                principalTable: "PermitTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_PermitTypes_RequiredPermitTypeId",
                table: "JobCards");

            migrationBuilder.DropTable(
                name: "JobCardPlannedParts");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_RequiredPermitTypeId",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "PartsRequired",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "PermitsRequired",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "RequiredPermitTypeId",
                table: "JobCards");
        }
    }
}
