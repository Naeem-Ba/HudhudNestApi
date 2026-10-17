using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceConversationStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpiredWaitingOn",
                table: "ServiceRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Intent",
                table: "ServiceRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: true);

            // Rows that existed before the Inquiry stage were requests from the start: they count as
            // confirmed (at their creation time) and their month starts at their last update.
            // Idempotent: only touches rows that are still unconfirmed, and only the two new columns.
            migrationBuilder.Sql(
                """
                UPDATE "ServiceRequests"
                SET "ConfirmedAt" = "CreatedAt",
                    "LastActivityAt" = "UpdatedAt"
                WHERE "ConfirmedAt" IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "ServiceRequestMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderParty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequestMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequestMessages_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceRequestMessages_UserAccounts_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Status_LastActivityAt",
                table: "ServiceRequests",
                columns: new[] { "Status", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequestMessages_SenderUserId",
                table: "ServiceRequestMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequestMessages_ServiceRequestId_CreatedAt",
                table: "ServiceRequestMessages",
                columns: new[] { "ServiceRequestId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceRequestMessages");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_Status_LastActivityAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "ExpiredAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "ExpiredWaitingOn",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Intent",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "ServiceRequests");
        }
    }
}
