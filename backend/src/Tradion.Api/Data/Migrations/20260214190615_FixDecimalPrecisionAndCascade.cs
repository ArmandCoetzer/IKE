using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixDecimalPrecisionAndCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards",
                column: "ServiceRequestId",
                principalTable: "ServiceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards");

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_ServiceRequests_ServiceRequestId",
                table: "JobCards",
                column: "ServiceRequestId",
                principalTable: "ServiceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_Sites_SiteId",
                table: "JobCards",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
