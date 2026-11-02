using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceHistoryCurrencyAndFollowerSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FollowersNotifiedAt",
                table: "PropertyPriceHistories",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldCurrencyCode",
                table: "PropertyPriceHistories",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            // Changes recorded before this release are history, not news: without this, the first
            // follower sweep after the deploy would announce every old price change at once.
            migrationBuilder.Sql(
                "UPDATE \"PropertyPriceHistories\" SET \"FollowersNotifiedAt\" = \"ChangedAt\" WHERE \"FollowersNotifiedAt\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPriceHistories_FollowersPending",
                table: "PropertyPriceHistories",
                column: "ChangedAt",
                filter: "\"FollowersNotifiedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PropertyPriceHistories_FollowersPending",
                table: "PropertyPriceHistories");

            migrationBuilder.DropColumn(
                name: "FollowersNotifiedAt",
                table: "PropertyPriceHistories");

            migrationBuilder.DropColumn(
                name: "OldCurrencyCode",
                table: "PropertyPriceHistories");
        }
    }
}
