using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedSearchAlertsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "SavedSearches",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Existing searches keep the only behavior they had: one alert per listing.
                defaultValue: "Instant");

            migrationBuilder.AddColumn<bool>(
                name: "HasBalcony",
                table: "SavedSearches",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasElevator",
                table: "SavedSearches",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasParkingSpace",
                table: "SavedSearches",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                table: "SavedSearches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNotifiedAt",
                table: "SavedSearches",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotifiedCount",
                table: "SavedSearches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "HasBalcony",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "HasElevator",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "HasParkingSpace",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "IsPaused",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "LastNotifiedAt",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "NotifiedCount",
                table: "SavedSearches");
        }
    }
}
