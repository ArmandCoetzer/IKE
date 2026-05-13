using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartUnitPriceAndIsLabour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLabour",
                table: "Parts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Parts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLabour",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Parts");
        }
    }
}
