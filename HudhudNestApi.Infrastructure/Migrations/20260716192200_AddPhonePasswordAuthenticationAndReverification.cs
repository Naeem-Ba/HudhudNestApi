using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhonePasswordAuthenticationAndReverification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastPhoneVerificationNotificationKey",
                table: "Users",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhoneNumber",
                table: "Users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhoneLastVerifiedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhoneVerificationDueAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhoneVerificationGraceEndsAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneVerificationState",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PhoneOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalizedPhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedPhoneNumber",
                table: "Users",
                column: "NormalizedPhoneNumber",
                unique: true,
                filter: "\"NormalizedPhoneNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneVerificationDueAtUtc",
                table: "Users",
                column: "PhoneVerificationDueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneOtpChallenges_ExpiresAtUtc",
                table: "PhoneOtpChallenges",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneOtpChallenges_NormalizedPhoneNumber_Purpose_CreatedAtU~",
                table: "PhoneOtpChallenges",
                columns: new[] { "NormalizedPhoneNumber", "Purpose", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneOtpChallenges");

            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedPhoneNumber",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PhoneVerificationDueAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastPhoneVerificationNotificationKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedPhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneLastVerifiedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneVerificationDueAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneVerificationGraceEndsAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneVerificationState",
                table: "Users");
        }
    }
}
