using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingExpiryWarningStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryWarningSentAt",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_ExpiresAt_PendingWarning",
                table: "Properties",
                column: "ExpiresAt",
                filter: "\"ExpiryWarningSentAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Status_ExpiresAt",
                table: "Properties",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Properties_ExpiresAt_PendingWarning",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_Status_ExpiresAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ExpiryWarningSentAt",
                table: "Properties");
        }
    }
}
