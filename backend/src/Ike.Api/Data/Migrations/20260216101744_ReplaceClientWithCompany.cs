using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ike.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceClientWithCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create Companies table first
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ParentCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Companies_ParentCompanyId",
                        column: x => x.ParentCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId");

            // 2. Copy existing Clients into Companies (Type=1 = Client)
            migrationBuilder.Sql(@"
                INSERT INTO Companies (Id, Name, Type, ContactEmail, ContactPhone, Address, IsActive, CreatedAt, UpdatedAt)
                SELECT Id, CompanyName, 1, Email, Phone, NULL, IsActive, CreatedAt, UpdatedAt FROM Clients
            ");

            // 3. Add CompanyId to AspNetUsers and backfill from Clients.UserId
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE AspNetUsers SET CompanyId = (SELECT Id FROM Clients WHERE Clients.UserId = AspNetUsers.Id)
            ");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Companies_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 4. Drop FKs to Clients so we can rename columns
            migrationBuilder.DropForeignKey(
                name: "FK_ClientBudgets_Clients_ClientId",
                table: "ClientBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Clients_ClientId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitTemplates_Clients_ClientId",
                table: "PermitTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Clients_ClientId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotes_Clients_ClientId",
                table: "Quotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Sites_Clients_ClientId",
                table: "Sites");

            // 5. Drop Clients table (CompanyId columns still hold the same GUIDs, now valid in Companies)
            migrationBuilder.DropTable(
                name: "Clients");

            // 6. Rename ClientId -> CompanyId
            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "Sites",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_Sites_ClientId",
                table: "Sites",
                newName: "IX_Sites_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "Quotes",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_Quotes_ClientId",
                table: "Quotes",
                newName: "IX_Quotes_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "PurchaseOrders",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrders_ClientId",
                table: "PurchaseOrders",
                newName: "IX_PurchaseOrders_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "PermitTemplates",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_PermitTemplates_ClientId",
                table: "PermitTemplates",
                newName: "IX_PermitTemplates_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "Invoices",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_Invoices_ClientId",
                table: "Invoices",
                newName: "IX_Invoices_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "ClientBudgets",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_ClientBudgets_ClientId",
                table: "ClientBudgets",
                newName: "IX_ClientBudgets_CompanyId");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('AspNetUsers', 'RegistrationStatus') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [RegistrationStatus] nvarchar(max) NULL;
                IF COL_LENGTH('AspNetUsers', 'Occupation') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [Occupation] nvarchar(max) NULL;
                IF COL_LENGTH('AspNetUsers', 'LastName') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [LastName] nvarchar(max) NULL;
                IF COL_LENGTH('AspNetUsers', 'InviteToken') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [InviteToken] nvarchar(max) NULL;
                IF COL_LENGTH('AspNetUsers', 'FirstName') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [FirstName] nvarchar(max) NULL;
            ");

            // 7. Add FKs from child tables to Companies
            migrationBuilder.AddForeignKey(
                name: "FK_ClientBudgets_Companies_CompanyId",
                table: "ClientBudgets",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Companies_CompanyId",
                table: "Invoices",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTemplates_Companies_CompanyId",
                table: "PermitTemplates",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Companies_CompanyId",
                table: "PurchaseOrders",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Companies_CompanyId",
                table: "Quotes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sites_Companies_CompanyId",
                table: "Sites",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Companies_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientBudgets_Companies_CompanyId",
                table: "ClientBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Companies_CompanyId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PermitTemplates_Companies_CompanyId",
                table: "PermitTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Companies_CompanyId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotes_Companies_CompanyId",
                table: "Quotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Sites_Companies_CompanyId",
                table: "Sites");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "Sites",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_Sites_CompanyId",
                table: "Sites",
                newName: "IX_Sites_ClientId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "Quotes",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_Quotes_CompanyId",
                table: "Quotes",
                newName: "IX_Quotes_ClientId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "PurchaseOrders",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrders_CompanyId",
                table: "PurchaseOrders",
                newName: "IX_PurchaseOrders_ClientId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "PermitTemplates",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_PermitTemplates_CompanyId",
                table: "PermitTemplates",
                newName: "IX_PermitTemplates_ClientId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "Invoices",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_Invoices_CompanyId",
                table: "Invoices",
                newName: "IX_Invoices_ClientId");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "ClientBudgets",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_ClientBudgets_CompanyId",
                table: "ClientBudgets",
                newName: "IX_ClientBudgets_ClientId");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('AspNetUsers', 'RegistrationStatus') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [RegistrationStatus] nvarchar(32) NULL;
                IF COL_LENGTH('AspNetUsers', 'Occupation') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [Occupation] nvarchar(200) NULL;
                IF COL_LENGTH('AspNetUsers', 'LastName') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [LastName] nvarchar(128) NULL;
                IF COL_LENGTH('AspNetUsers', 'InviteToken') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [InviteToken] nvarchar(64) NULL;
                IF COL_LENGTH('AspNetUsers', 'FirstName') IS NOT NULL
                    ALTER TABLE [AspNetUsers] ALTER COLUMN [FirstName] nvarchar(128) NULL;
            ");

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clients_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_UserId",
                table: "Clients",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientBudgets_Clients_ClientId",
                table: "ClientBudgets",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Clients_ClientId",
                table: "Invoices",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PermitTemplates_Clients_ClientId",
                table: "PermitTemplates",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Clients_ClientId",
                table: "PurchaseOrders",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Clients_ClientId",
                table: "Quotes",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sites_Clients_ClientId",
                table: "Sites",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id");
        }
    }
}
