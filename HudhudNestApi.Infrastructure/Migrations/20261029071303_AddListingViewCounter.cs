using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingViewCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListingViewCounters",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingViewCounters", x => new { x.Kind, x.ListingId });
                    table.CheckConstraint("CK_ListingViewCounters_ViewCount_NonNegative", "\"ViewCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ListingViewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Bucket = table.Column<long>(type: "bigint", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingViewEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListingViewEvents_Listing_ViewedAt",
                table: "ListingViewEvents",
                columns: new[] { "Kind", "ListingId", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingViewEvents_ViewedAt",
                table: "ListingViewEvents",
                column: "ViewedAt");

            migrationBuilder.CreateIndex(
                name: "UX_ListingViewEvents_Dedupe",
                table: "ListingViewEvents",
                columns: new[] { "Kind", "ListingId", "ViewerKeyHash", "Bucket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingViewCounters");

            migrationBuilder.DropTable(
                name: "ListingViewEvents");
        }
    }
}
