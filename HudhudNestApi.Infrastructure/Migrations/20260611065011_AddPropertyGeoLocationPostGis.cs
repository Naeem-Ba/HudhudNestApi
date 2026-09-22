using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations;

public partial class AddPropertyGeoLocationPostGis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE EXTENSION IF NOT EXISTS postgis;
        """);

        migrationBuilder.Sql("""
            ALTER TABLE "Properties"
            ADD COLUMN IF NOT EXISTS "GeoLocation" geography(Point, 4326)
            GENERATED ALWAYS AS (
                CASE
                    WHEN "Latitude" IS NOT NULL AND "Longitude" IS NOT NULL
                    THEN ST_SetSRID(ST_MakePoint("Longitude", "Latitude"), 4326)::geography
                    ELSE NULL
                END
            ) STORED;
        """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Properties_GeoLocation"
            ON "Properties"
            USING GIST ("GeoLocation");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Properties_GeoLocation";
        """);

        migrationBuilder.Sql("""
            ALTER TABLE "Properties"
            DROP COLUMN IF EXISTS "GeoLocation";
        """);
    }
}