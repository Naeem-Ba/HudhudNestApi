using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShortStayListingTakeDown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TakenDownAt",
                table: "ShortStayListings",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TakenDownByUserId",
                table: "ShortStayListings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakenDownReason",
                table: "ShortStayListings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_TakenDownAt",
                table: "ShortStayListings",
                column: "TakenDownAt",
                filter: "\"TakenDownAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShortStayListings_TakenDownAt",
                table: "ShortStayListings");

            migrationBuilder.DropColumn(
                name: "TakenDownAt",
                table: "ShortStayListings");

            migrationBuilder.DropColumn(
                name: "TakenDownByUserId",
                table: "ShortStayListings");

            migrationBuilder.DropColumn(
                name: "TakenDownReason",
                table: "ShortStayListings");
        }
    }
}
