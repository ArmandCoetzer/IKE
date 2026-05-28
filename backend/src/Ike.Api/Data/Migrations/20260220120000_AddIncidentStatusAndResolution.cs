using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    [Migration("20260220120000_AddIncidentStatusAndResolution")]
    public partial class AddIncidentStatusAndResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "IncidentReports",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "IncidentReports",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Open");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "IncidentReports");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "IncidentReports");
        }
    }
}
