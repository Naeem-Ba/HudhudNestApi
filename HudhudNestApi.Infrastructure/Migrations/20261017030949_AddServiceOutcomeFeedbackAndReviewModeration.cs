using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOutcomeFeedbackAndReviewModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "ServiceReviews",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenReason",
                table: "ServiceReviews",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "ServiceReviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotCompletedAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeNote",
                table: "ServiceRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeReason",
                table: "ServiceRequests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterAnswer",
                table: "ServiceRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequesterFeedbackAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterFeedbackNote",
                table: "ServiceRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterReason",
                table: "ServiceRequests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "ServiceReviews");

            migrationBuilder.DropColumn(
                name: "HiddenReason",
                table: "ServiceReviews");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "ServiceReviews");

            migrationBuilder.DropColumn(
                name: "NotCompletedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "OutcomeNote",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "OutcomeReason",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RequesterAnswer",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RequesterFeedbackAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RequesterFeedbackNote",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "RequesterReason",
                table: "ServiceRequests");
        }
    }
}
