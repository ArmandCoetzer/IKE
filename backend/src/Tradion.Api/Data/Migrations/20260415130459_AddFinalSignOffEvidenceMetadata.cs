using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalSignOffEvidenceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FinalClientSignOffCaptureSource",
                table: "JobCards",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalClientSignOffFileSha256",
                table: "JobCards",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalClientSignOffCaptureSource",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "FinalClientSignOffFileSha256",
                table: "JobCards");
        }
    }
}
