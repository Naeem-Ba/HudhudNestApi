using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListingTranslationsShortStay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ListingTranslations_PropertyId_Field_TargetLanguage",
                table: "ListingTranslations");

            migrationBuilder.AlterColumn<Guid>(
                name: "PropertyId",
                table: "ListingTranslations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ShortStayListingId",
                table: "ListingTranslations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingTranslations_PropertyId_Field_TargetLanguage",
                table: "ListingTranslations",
                columns: new[] { "PropertyId", "Field", "TargetLanguage" },
                unique: true,
                filter: "\"PropertyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ListingTranslations_ShortStayListingId_Field_TargetLanguage",
                table: "ListingTranslations",
                columns: new[] { "ShortStayListingId", "Field", "TargetLanguage" },
                unique: true,
                filter: "\"ShortStayListingId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ListingTranslations_ExactlyOneListing",
                table: "ListingTranslations",
                sql: "(\"PropertyId\" IS NOT NULL AND \"ShortStayListingId\" IS NULL) OR (\"PropertyId\" IS NULL AND \"ShortStayListingId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ListingTranslations_ShortStayListings_ShortStayListingId",
                table: "ListingTranslations",
                column: "ShortStayListingId",
                principalTable: "ShortStayListings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ListingTranslations_ShortStayListings_ShortStayListingId",
                table: "ListingTranslations");

            migrationBuilder.DropIndex(
                name: "IX_ListingTranslations_PropertyId_Field_TargetLanguage",
                table: "ListingTranslations");

            migrationBuilder.DropIndex(
                name: "IX_ListingTranslations_ShortStayListingId_Field_TargetLanguage",
                table: "ListingTranslations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ListingTranslations_ExactlyOneListing",
                table: "ListingTranslations");

            migrationBuilder.DropColumn(
                name: "ShortStayListingId",
                table: "ListingTranslations");

            migrationBuilder.AlterColumn<Guid>(
                name: "PropertyId",
                table: "ListingTranslations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingTranslations_PropertyId_Field_TargetLanguage",
                table: "ListingTranslations",
                columns: new[] { "PropertyId", "Field", "TargetLanguage" },
                unique: true);
        }
    }
}
