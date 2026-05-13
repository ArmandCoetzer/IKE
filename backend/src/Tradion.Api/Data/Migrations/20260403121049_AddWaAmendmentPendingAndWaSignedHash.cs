using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWaAmendmentPendingAndWaSignedHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PendingWaAmendmentSignOff",
                table: "JobPermits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WaSignedBusinessContentHash",
                table: "JobPermits",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingWaAmendmentSignOff",
                table: "JobPermits");

            migrationBuilder.DropColumn(
                name: "WaSignedBusinessContentHash",
                table: "JobPermits");
        }
    }
}
