using System;
using Ike.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260524144500_AddUploadedQuoteSupport")]
    public partial class AddUploadedQuoteSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedQuoteNumber",
                table: "Quotes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedSupplierName",
                table: "Quotes",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "Quotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsUploaded",
                table: "Quotes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadedAt",
                table: "Quotes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedContentType",
                table: "Quotes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedFileName",
                table: "Quotes",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedFilePath",
                table: "Quotes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExtractedQuoteNumber", table: "Quotes");
            migrationBuilder.DropColumn(name: "ExtractedSupplierName", table: "Quotes");
            migrationBuilder.DropColumn(name: "ExtractedText", table: "Quotes");
            migrationBuilder.DropColumn(name: "IsUploaded", table: "Quotes");
            migrationBuilder.DropColumn(name: "UploadedAt", table: "Quotes");
            migrationBuilder.DropColumn(name: "UploadedContentType", table: "Quotes");
            migrationBuilder.DropColumn(name: "UploadedFileName", table: "Quotes");
            migrationBuilder.DropColumn(name: "UploadedFilePath", table: "Quotes");
        }
    }
}
