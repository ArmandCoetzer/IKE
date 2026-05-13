using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    [Migration("20260220120100_AddJobCardActivePermit")]
    public partial class AddJobCardActivePermit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveJobPermitId",
                table: "JobCards",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_ActiveJobPermitId",
                table: "JobCards",
                column: "ActiveJobPermitId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_JobPermits_ActiveJobPermitId",
                table: "JobCards",
                column: "ActiveJobPermitId",
                principalTable: "JobPermits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_JobPermits_ActiveJobPermitId",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_ActiveJobPermitId",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "ActiveJobPermitId",
                table: "JobCards");
        }
    }
}
