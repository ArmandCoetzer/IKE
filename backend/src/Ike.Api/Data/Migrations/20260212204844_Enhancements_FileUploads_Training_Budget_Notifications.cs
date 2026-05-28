using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Enhancements_FileUploads_Training_Budget_Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CompletedAt",
                table: "UserModuleProgress",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<int>(
                name: "VideoProgressPercent",
                table: "UserModuleProgress",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContinuationApprovedAt",
                table: "ClientBudgets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContinuationApprovedByUserId",
                table: "ClientBudgets",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentAttachments_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientBudgets_ContinuationApprovedByUserId",
                table: "ClientBudgets",
                column: "ContinuationApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAttachments_EquipmentId",
                table: "EquipmentAttachments",
                column: "EquipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientBudgets_AspNetUsers_ContinuationApprovedByUserId",
                table: "ClientBudgets",
                column: "ContinuationApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientBudgets_AspNetUsers_ContinuationApprovedByUserId",
                table: "ClientBudgets");

            migrationBuilder.DropTable(
                name: "EquipmentAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ClientBudgets_ContinuationApprovedByUserId",
                table: "ClientBudgets");

            migrationBuilder.DropColumn(
                name: "VideoProgressPercent",
                table: "UserModuleProgress");

            migrationBuilder.DropColumn(
                name: "ContinuationApprovedAt",
                table: "ClientBudgets");

            migrationBuilder.DropColumn(
                name: "ContinuationApprovedByUserId",
                table: "ClientBudgets");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CompletedAt",
                table: "UserModuleProgress",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
