using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingTakeDown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TakenDownAt",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TakenDownByUserId",
                table: "Properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakenDownReason",
                table: "Properties",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_TakenDownAt",
                table: "Properties",
                column: "TakenDownAt",
                filter: "\"TakenDownAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Properties_TakenDownAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "TakenDownAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "TakenDownByUserId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "TakenDownReason",
                table: "Properties");
        }
    }
}
