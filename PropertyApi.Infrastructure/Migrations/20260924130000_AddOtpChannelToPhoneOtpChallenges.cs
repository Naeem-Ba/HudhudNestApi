using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpChannelToPhoneOtpChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "PhoneOtpChallenges",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sms");

            migrationBuilder.AddColumn<string>(
                name: "ProviderRequestId",
                table: "PhoneOtpChallenges",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "PhoneOtpChallenges");

            migrationBuilder.DropColumn(
                name: "ProviderRequestId",
                table: "PhoneOtpChallenges");
        }
    }
}
