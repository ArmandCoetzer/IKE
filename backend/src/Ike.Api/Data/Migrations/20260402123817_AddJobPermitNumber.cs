using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPermitNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PermitNumber",
                table: "JobPermits",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermitNumber",
                table: "JobPermits");
        }
    }
}
