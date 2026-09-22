using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyManualReviewFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ManualReviewFlaggedAt",
                table: "Agencies",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualReviewReason",
                table: "Agencies",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresManualReview",
                table: "Agencies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_RequiresManualReview",
                table: "Agencies",
                column: "RequiresManualReview");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Agencies_RequiresManualReview",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "ManualReviewFlaggedAt",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "ManualReviewReason",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "RequiresManualReview",
                table: "Agencies");
        }
    }
}
