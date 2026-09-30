using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HudhudNestApi.Infrastructure.Persistence;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260611193000_AddPropertyTextSearchTrigramIndexes")]
public partial class AddPropertyTextSearchTrigramIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE EXTENSION IF NOT EXISTS pg_trgm;
        """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Properties_City_Trigram"
            ON "Properties" USING GIN ("City" gin_trgm_ops);
        """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Properties_Region_Trigram"
            ON "Properties" USING GIN ("Region" gin_trgm_ops);
        """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Properties_Active_Search"
            ON "Properties" ("Status", "IsPublished", "CreatedAt")
            WHERE "IsDeleted" = FALSE AND "IsPublished" = TRUE;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Properties_Active_Search";
        """);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Properties_Region_Trigram";
        """);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Properties_City_Trigram";
        """);
    }
}
