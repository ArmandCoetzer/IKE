using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    public partial class AddPartSuppliersAndSupplierQuoteRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartSuppliers",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartSuppliers", x => new { x.PartId, x.SupplierId });
                    table.ForeignKey(
                        name: "FK_PartSuppliers_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartSuppliers_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierQuoteRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedQuantity = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierQuoteRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierQuoteRequests_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierQuoteRequests_JobCards_JobCardId",
                        column: x => x.JobCardId,
                        principalTable: "JobCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierQuoteRequests_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierQuoteRequests_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartSuppliers_SupplierId",
                table: "PartSuppliers",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierQuoteRequests_CreatedById",
                table: "SupplierQuoteRequests",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierQuoteRequests_JobCardId",
                table: "SupplierQuoteRequests",
                column: "JobCardId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierQuoteRequests_PartId",
                table: "SupplierQuoteRequests",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierQuoteRequests_SupplierId",
                table: "SupplierQuoteRequests",
                column: "SupplierId");

            migrationBuilder.Sql(@"
                INSERT INTO PartSuppliers (PartId, SupplierId, LinkedAt)
                SELECT p.Id, p.SupplierId, GETUTCDATE()
                FROM Parts p
                WHERE p.SupplierId IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM PartSuppliers ps WHERE ps.PartId = p.Id AND ps.SupplierId = p.SupplierId
                  )
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartSuppliers");

            migrationBuilder.DropTable(
                name: "SupplierQuoteRequests");
        }
    }
}
