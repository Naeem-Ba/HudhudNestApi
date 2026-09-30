using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUtmAttributionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "PropertyShareEvents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmContent",
                table: "PropertyShareEvents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "PropertyShareEvents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "PropertyShareEvents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PropertyAttributionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UtmSource = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    UtmMedium = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    UtmCampaign = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    UtmContent = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyAttributionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyAttributionEvents_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyShareEvents_UtmCampaign",
                table: "PropertyShareEvents",
                column: "UtmCampaign");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyShareEvents_UtmSource",
                table: "PropertyShareEvents",
                column: "UtmSource");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_CreatedAt",
                table: "PropertyAttributionEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_EventType",
                table: "PropertyAttributionEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_PropertyId",
                table: "PropertyAttributionEvents",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_PropertyId_EventType",
                table: "PropertyAttributionEvents",
                columns: new[] { "PropertyId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_UtmCampaign",
                table: "PropertyAttributionEvents",
                column: "UtmCampaign");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttributionEvents_UtmSource",
                table: "PropertyAttributionEvents",
                column: "UtmSource");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PropertyAttributionEvents");

            migrationBuilder.DropIndex(
                name: "IX_PropertyShareEvents_UtmCampaign",
                table: "PropertyShareEvents");

            migrationBuilder.DropIndex(
                name: "IX_PropertyShareEvents_UtmSource",
                table: "PropertyShareEvents");

            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "PropertyShareEvents");

            migrationBuilder.DropColumn(
                name: "UtmContent",
                table: "PropertyShareEvents");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "PropertyShareEvents");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "PropertyShareEvents");
        }
    }
}
