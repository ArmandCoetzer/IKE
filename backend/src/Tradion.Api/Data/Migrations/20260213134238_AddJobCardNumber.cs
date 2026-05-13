using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCardNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobCardNumber",
                table: "JobCards",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JobCardNumber",
                table: "JobCards");
        }
    }
}
