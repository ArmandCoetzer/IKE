using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToJobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "JobTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTypes_CompanyId",
                table: "JobTypes",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobTypes_Companies_CompanyId",
                table: "JobTypes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobTypes_Companies_CompanyId",
                table: "JobTypes");

            migrationBuilder.DropIndex(
                name: "IX_JobTypes_CompanyId",
                table: "JobTypes");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "JobTypes");
        }
    }
}
