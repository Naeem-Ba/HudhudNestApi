using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// QA Phase 7. ClosedAt/ImagesCompactedAt track closed deals (Sold/Rented) for the photo
    /// compression sweep. SearchText is a stored generated column holding the title, description
    /// and place names with Arabic spelling variants folded together, served by a trigram index.
    /// Adding a stored generated column rewrites the Properties table once, under a lock; the
    /// table is small (thousands of rows) so this takes well under a second, but deploy it in a
    /// quiet moment. The Down path drops the index first, then the columns.
    /// </summary>
    public partial class AddListingClosedDealAndSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImagesCompactedAt",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Properties",
                type: "text",
                nullable: false,
                computedColumnSql: "translate(regexp_replace(coalesce(\"Title\", '') || ' ' || coalesce(\"Description\", '') || ' ' || coalesce(\"City\", '') || ' ' || coalesce(\"Region\", '') || ' ' || coalesce(\"DistrictText\", '') || ' ' || coalesce(\"NeighborhoodText\", ''), '[\\u064B-\\u065F\\u0670\\u0640]', '', 'g'), 'أإآٱىةؤئ٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايهوي01234567890123456789')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_ClosedAt_PendingCompaction",
                table: "Properties",
                column: "ClosedAt",
                filter: "\"ClosedAt\" IS NOT NULL AND \"ImagesCompactedAt\" IS NULL");

            // The model cannot describe an operator class, so (like the City/Region/Title/Description
            // trigram indexes) the keyword index is created in SQL. pg_trgm is already installed by
            // 20260611193000_AddPropertyTextSearchTrigramIndexes.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
            """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Properties_SearchText_Trigram"
                ON "Properties" USING GIN ("SearchText" gin_trgm_ops);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Properties_SearchText_Trigram";
            """);

            migrationBuilder.DropIndex(
                name: "IX_Properties_ClosedAt_PendingCompaction",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ImagesCompactedAt",
                table: "Properties");
        }
    }
}
