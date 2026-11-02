using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListingTranslationStreetField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ListingTranslations_Field",
                table: "ListingTranslations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ListingTranslations_Field",
                table: "ListingTranslations",
                sql: "\"Field\" IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Street rows are derived data and would violate the old constraint.
            migrationBuilder.Sql("DELETE FROM \"ListingTranslations\" WHERE \"Field\" = 3;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ListingTranslations_Field",
                table: "ListingTranslations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ListingTranslations_Field",
                table: "ListingTranslations",
                sql: "\"Field\" IN (1, 2)");
        }
    }
}
