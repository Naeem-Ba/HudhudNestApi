using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingLocationPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HideExactLocation",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Map_LatLng",
                table: "Properties",
                columns: new[] { "Latitude", "Longitude" },
                filter: "\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL AND \"IsPublished\" AND NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Properties_Map_LatLng",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HideExactLocation",
                table: "Properties");
        }
    }
}
