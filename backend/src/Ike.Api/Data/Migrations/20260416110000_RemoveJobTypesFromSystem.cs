using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    public partial class RemoveJobTypesFromSystem : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobTypeRequiredBadges");

            migrationBuilder.DropTable(
                name: "JobTypes");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_JobTypeId",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "JobTypeId",
                table: "JobCards");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JobTypeId",
                table: "JobCards",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobTypeRequiredBadges",
                columns: table => new
                {
                    JobTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BadgeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTypeRequiredBadges", x => new { x.JobTypeId, x.BadgeId });
                    table.ForeignKey(
                        name: "FK_JobTypeRequiredBadges_Badges_BadgeId",
                        column: x => x.BadgeId,
                        principalTable: "Badges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobTypeRequiredBadges_JobTypes_JobTypeId",
                        column: x => x.JobTypeId,
                        principalTable: "JobTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_JobTypeId",
                table: "JobCards",
                column: "JobTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobTypes_CompanyId",
                table: "JobTypes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_JobTypeRequiredBadges_BadgeId",
                table: "JobTypeRequiredBadges",
                column: "BadgeId");
        }
    }
}
