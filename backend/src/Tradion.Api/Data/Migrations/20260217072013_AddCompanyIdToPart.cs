using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Parts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parts_CompanyId",
                table: "Parts",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Parts_Companies_CompanyId",
                table: "Parts",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Parts_Companies_CompanyId",
                table: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_Parts_CompanyId",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Parts");
        }
    }
}
