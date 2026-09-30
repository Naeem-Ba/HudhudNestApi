using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountDeletionSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionRequestedAt",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionScheduledFor",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_DeletionScheduledFor_Pending",
                table: "UserAccounts",
                column: "DeletionScheduledFor",
                filter: "\"DeletionScheduledFor\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_DeletionScheduledFor_Pending",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "DeletionScheduledFor",
                table: "UserAccounts");
        }
    }
}
