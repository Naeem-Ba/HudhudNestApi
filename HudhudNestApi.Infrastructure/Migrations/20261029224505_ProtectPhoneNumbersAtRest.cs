using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// DB security audit 2026-10-29 (S01): OTP challenges keep the number encrypted and are searched
    /// by its lookup hash. Schema only; the clear-text copies on Users are removed later by the
    /// phone lookup backfill (Migrator --backfill-phone-lookup-hash=true), once this API is live.
    /// </summary>
    public partial class ProtectPhoneNumbersAtRest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneOtpChallenges_NormalizedPhoneNumber_Purpose_CreatedAtU~",
                table: "PhoneOtpChallenges");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedPhoneNumber",
                table: "PhoneOtpChallenges",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumberLookupHash",
                table: "PhoneOtpChallenges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneOtpChallenges_PhoneNumberLookupHash_Purpose_CreatedAtU~",
                table: "PhoneOtpChallenges",
                columns: new[] { "PhoneNumberLookupHash", "Purpose", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneOtpChallenges_PhoneNumberLookupHash_Purpose_CreatedAtU~",
                table: "PhoneOtpChallenges");

            migrationBuilder.DropColumn(
                name: "PhoneNumberLookupHash",
                table: "PhoneOtpChallenges");

            // Encrypted numbers do not fit the old 16 characters. Challenges live five minutes, so the
            // ones written by the newer code are dropped rather than the shrink failing.
            migrationBuilder.Sql("DELETE FROM \"PhoneOtpChallenges\" WHERE length(\"NormalizedPhoneNumber\") > 16;");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedPhoneNumber",
                table: "PhoneOtpChallenges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneOtpChallenges_NormalizedPhoneNumber_Purpose_CreatedAtU~",
                table: "PhoneOtpChallenges",
                columns: new[] { "NormalizedPhoneNumber", "Purpose", "CreatedAtUtc" });
        }
    }
}
