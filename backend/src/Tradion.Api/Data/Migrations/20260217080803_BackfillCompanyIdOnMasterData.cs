using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tradion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCompanyIdOnMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Assign existing JobTypes, Parts, and PermitTypes with null CompanyId to the first main company.
            // Main companies have ParentCompanyId = null. This ensures legacy data appears in scoped lists.
            var backfillSql = @"
                DECLARE @MainCompanyId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM Companies WHERE ParentCompanyId IS NULL);
                IF @MainCompanyId IS NOT NULL
                BEGIN
                    UPDATE JobTypes SET CompanyId = @MainCompanyId WHERE CompanyId IS NULL;
                    UPDATE Parts SET CompanyId = @MainCompanyId WHERE CompanyId IS NULL;
                    UPDATE PermitTypes SET CompanyId = @MainCompanyId WHERE CompanyId IS NULL;
                END";
            migrationBuilder.Sql(backfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data migration cannot be safely reverted - which records were backfilled is not tracked.
        }
    }
}
