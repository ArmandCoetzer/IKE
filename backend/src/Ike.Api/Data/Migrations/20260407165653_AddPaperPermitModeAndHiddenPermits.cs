using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperPermitModeAndHiddenPermits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HiddenFromUiForHistory",
                table: "JobPermits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaperClientSignedOffAt",
                table: "JobPermits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaperClientSignedOffByUserId",
                table: "JobPermits",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaperPermitNumber",
                table: "JobPermits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaperModeActivatedAt",
                table: "JobCards",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaperModeActivatedByUserId",
                table: "JobCards",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaperPermitMode",
                table: "JobCards",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenFromUiForHistory",
                table: "JobPermits");

            migrationBuilder.DropColumn(
                name: "PaperClientSignedOffAt",
                table: "JobPermits");

            migrationBuilder.DropColumn(
                name: "PaperClientSignedOffByUserId",
                table: "JobPermits");

            migrationBuilder.DropColumn(
                name: "PaperPermitNumber",
                table: "JobPermits");

            migrationBuilder.DropColumn(
                name: "PaperModeActivatedAt",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "PaperModeActivatedByUserId",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "PaperPermitMode",
                table: "JobCards");
        }
    }
}
