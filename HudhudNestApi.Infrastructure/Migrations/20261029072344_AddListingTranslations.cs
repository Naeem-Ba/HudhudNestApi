using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListingTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<int>(type: "integer", nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SourceLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TranslatedText = table.Column<string>(type: "text", nullable: true),
                    SourceCharacters = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RetryAfter = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingTranslations", x => x.Id);
                    table.CheckConstraint("CK_ListingTranslations_CompletedHasText", "(\"Status\" = 2 AND \"TranslatedText\" IS NOT NULL AND char_length(btrim(\"TranslatedText\")) > 0) OR (\"Status\" <> 2 AND \"TranslatedText\" IS NULL)");
                    table.CheckConstraint("CK_ListingTranslations_Field", "\"Field\" IN (1, 2)");
                    table.CheckConstraint("CK_ListingTranslations_SourceHash", "char_length(\"SourceHash\") = 64");
                    table.CheckConstraint("CK_ListingTranslations_Status", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_ListingTranslations_TargetLanguage", "\"TargetLanguage\" IN ('ar', 'de', 'en')");
                    table.ForeignKey(
                        name: "FK_ListingTranslations_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TranslationMonthlyUsages",
                columns: table => new
                {
                    Month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Characters = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationMonthlyUsages", x => x.Month);
                    table.CheckConstraint("CK_TranslationMonthlyUsages_Characters", "\"Characters\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListingTranslations_PropertyId_Field_TargetLanguage",
                table: "ListingTranslations",
                columns: new[] { "PropertyId", "Field", "TargetLanguage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingTranslations");

            migrationBuilder.DropTable(
                name: "TranslationMonthlyUsages");
        }
    }
}
