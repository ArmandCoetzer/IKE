using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierOwnershipScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Suppliers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE s
                SET s.CompanyId = x.CompanyId
                FROM Suppliers s
                CROSS APPLY (
                    SELECT TOP (1) p.CompanyId
                    FROM Parts p
                    WHERE p.SupplierId = s.Id AND p.CompanyId IS NOT NULL
                    ORDER BY p.CreatedAt ASC
                ) x
                WHERE s.CompanyId IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE s
                SET s.CompanyId = x.CompanyId
                FROM Suppliers s
                CROSS APPLY (
                    SELECT TOP (1) p.CompanyId
                    FROM PartSuppliers ps
                    INNER JOIN Parts p ON p.Id = ps.PartId
                    WHERE ps.SupplierId = s.Id AND p.CompanyId IS NOT NULL
                    ORDER BY ps.LinkedAt ASC
                ) x
                WHERE s.CompanyId IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CompanyId",
                table: "Suppliers",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Companies_CompanyId",
                table: "Suppliers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Companies_CompanyId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_CompanyId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Suppliers");
        }
    }
}
