using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlansAndUserPlanSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "UserAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanSelectedAt",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TaglineKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PriceKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriceUsd = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    FeatureKeys = table.Column<string[]>(type: "text[]", nullable: false),
                    NoteKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CtaKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsRecommended = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_PlanId",
                table: "UserAccounts",
                column: "PlanId",
                filter: "\"PlanId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_IsActive",
                table: "Plans",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Tier",
                table: "Plans",
                column: "Tier",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_Plans_PlanId",
                table: "UserAccounts",
                column: "PlanId",
                principalTable: "Plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_Plans_PlanId",
                table: "UserAccounts");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_PlanId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PlanSelectedAt",
                table: "UserAccounts");
        }
    }
}
