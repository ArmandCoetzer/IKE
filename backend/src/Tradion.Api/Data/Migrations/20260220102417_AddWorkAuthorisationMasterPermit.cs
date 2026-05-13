using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAuthorisationMasterPermit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWorkAuthorisation",
                table: "PermitTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TriggersPermitTypeIdsJson",
                table: "PermitTypes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MasterPermitId",
                table: "JobPermits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPermits_MasterPermitId",
                table: "JobPermits",
                column: "MasterPermitId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobPermits_JobPermits_MasterPermitId",
                table: "JobPermits",
                column: "MasterPermitId",
                principalTable: "JobPermits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobPermits_JobPermits_MasterPermitId",
                table: "JobPermits");

            migrationBuilder.DropIndex(
                name: "IX_JobPermits_MasterPermitId",
                table: "JobPermits");

            migrationBuilder.DropColumn(
                name: "IsWorkAuthorisation",
                table: "PermitTypes");

            migrationBuilder.DropColumn(
                name: "TriggersPermitTypeIdsJson",
                table: "PermitTypes");

            migrationBuilder.DropColumn(
                name: "MasterPermitId",
                table: "JobPermits");
        }
    }
}
