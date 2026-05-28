using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPermitMasterLinksAndLazyRollover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobPermitMasterLinks",
                columns: table => new
                {
                    MasterPermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChildPermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPermitMasterLinks", x => new { x.MasterPermitId, x.ChildPermitId });
                    table.ForeignKey(
                        name: "FK_JobPermitMasterLinks_JobPermits_ChildPermitId",
                        column: x => x.ChildPermitId,
                        principalTable: "JobPermits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobPermitMasterLinks_JobPermits_MasterPermitId",
                        column: x => x.MasterPermitId,
                        principalTable: "JobPermits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPermitMasterLinks_ChildPermitId",
                table: "JobPermitMasterLinks",
                column: "ChildPermitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobPermitMasterLinks");
        }
    }
}
