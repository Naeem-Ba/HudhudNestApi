using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HudhudNestApi.Infrastructure.Persistence;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations;

/// <summary>
/// The keyword search (PropertyRepository) filters with ILIKE '%term%' on Title and Description, which no
/// btree index can serve, so every search scanned the table. Same shape as
/// 20260611193000_AddPropertyTextSearchTrigramIndexes, which covered City/Region only. Index-only: no model change.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260924120000_AddPropertyTitleDescriptionTrigramIndexes")]
public partial class AddPropertyTitleDescriptionTrigramIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE EXTENSION IF NOT EXISTS pg_trgm;
        """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Properties_Title_Trigram"
            ON "Properties" USING GIN ("Title" gin_trgm_ops);
        """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Properties_Description_Trigram"
            ON "Properties" USING GIN ("Description" gin_trgm_ops);
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Properties_Description_Trigram";
        """);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Properties_Title_Trigram";
        """);
    }
}
