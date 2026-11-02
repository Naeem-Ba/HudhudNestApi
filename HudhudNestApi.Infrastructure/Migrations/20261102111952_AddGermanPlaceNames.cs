using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGermanPlaceNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameDe",
                table: "Neighborhoods",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameDe",
                table: "Governorates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameDe",
                table: "Districts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Established German names of the 14 governorates; districts and neighborhoods stay NULL
            // (clients fall back to the English transliteration) until someone enters a German name.
            migrationBuilder.Sql("""
                UPDATE "Governorates" g SET "NameDe" = v.de
                FROM (VALUES
                ('Damascus', 'Damaskus'),
                ('Rural Damascus', 'Umland von Damaskus'),
                ('Aleppo', 'Aleppo'),
                ('Homs', 'Homs'),
                ('Hama', 'Hama'),
                ('Latakia', 'Latakia'),
                ('Tartus', 'Tartus'),
                ('Al-Hasakah', 'Hasaka'),
                ('Deir ez-Zor', 'Deir ez-Zor'),
                ('Raqqa', 'Rakka'),
                ('Idlib', 'Idlib'),
                ('Daraa', 'Daraa'),
                ('Quneitra', 'Kuneitra'),
                ('As-Suwayda', 'As-Suwaida')
                ) AS v(en, de)
                WHERE g."NameEn" = v.en AND g."NameDe" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameDe",
                table: "Neighborhoods");

            migrationBuilder.DropColumn(
                name: "NameDe",
                table: "Governorates");

            migrationBuilder.DropColumn(
                name: "NameDe",
                table: "Districts");
        }
    }
}
