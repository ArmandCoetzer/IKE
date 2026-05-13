using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalClientSignOffOnJobCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinalClientSignOffAt",
                table: "JobCards",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalClientSignOffByUserId",
                table: "JobCards",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalClientSignOffAt",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "FinalClientSignOffByUserId",
                table: "JobCards");
        }
    }
}
