using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260728211500_AddPublishedPropertyGeoLocationPartialIndex")]
public sealed class AddPublishedPropertyGeoLocationPartialIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Properties_GeoLocation_Published"
            ON "Properties" USING GIST ("GeoLocation")
            WHERE "IsPublished" = TRUE
              AND "IsDeleted" = FALSE
              AND "GeoLocation" IS NOT NULL;
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX CONCURRENTLY IF EXISTS "IX_Properties_GeoLocation_Published";
            """,
            suppressTransaction: true);
    }
}
