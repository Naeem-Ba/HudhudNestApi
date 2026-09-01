using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountSubscriptionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanActivationSource",
                table: "UserAccounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanCancelledAt",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanExpiresAt",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanGrantedByUserId",
                table: "UserAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanStatus",
                table: "UserAccounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_PlanGrantedByUserId",
                table: "UserAccounts",
                column: "PlanGrantedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_Users_PlanGrantedByUserId",
                table: "UserAccounts",
                column: "PlanGrantedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_Users_PlanGrantedByUserId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_PlanGrantedByUserId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanActivationSource",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanCancelledAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanExpiresAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanGrantedByUserId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanStatus",
                table: "UserAccounts");
        }
    }
}
