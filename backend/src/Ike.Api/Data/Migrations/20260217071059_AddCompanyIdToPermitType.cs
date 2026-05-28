using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToPermitType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "PermitTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitTypes_CompanyId",
                table: "PermitTypes",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTypes_Companies_CompanyId",
                table: "PermitTypes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PermitTypes_Companies_CompanyId",
                table: "PermitTypes");

            migrationBuilder.DropIndex(
                name: "IX_PermitTypes_CompanyId",
                table: "PermitTypes");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PermitTypes");
        }
    }
}
