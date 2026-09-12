using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddValuationNotificationIdempotencyAndReminderStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryNotifiedAt",
                table: "ValuationOfficeInvitations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryNotifiedAt",
                table: "ValuationInquiries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "ValuationInquiries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultReadyNotifiedAt",
                table: "ValuationInquiries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_Status_CreatedAt",
                table: "ValuationInquiries",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ValuationInquiries_Status_CreatedAt",
                table: "ValuationInquiries");

            migrationBuilder.DropColumn(
                name: "ExpiryNotifiedAt",
                table: "ValuationOfficeInvitations");

            migrationBuilder.DropColumn(
                name: "ExpiryNotifiedAt",
                table: "ValuationInquiries");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "ValuationInquiries");

            migrationBuilder.DropColumn(
                name: "ResultReadyNotifiedAt",
                table: "ValuationInquiries");
        }
    }
}
