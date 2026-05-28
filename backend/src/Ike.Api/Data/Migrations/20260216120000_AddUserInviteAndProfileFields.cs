using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    public partial class AddUserInviteAndProfileFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                table: "AspNetUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "RegistrationStatus",
                table: "AspNetUsers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "InviteToken",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "InviteTokenExpiry",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FirstName", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "LastName", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "Occupation", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "RegistrationStatus", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "InviteToken", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "InviteTokenExpiry", table: "AspNetUsers");
        }
    }
}
