using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ListingReportNumberSeq");

            migrationBuilder.CreateTable(
                name: "ListingReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"ListingReportNumberSeq\"')"),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingTitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    AssignedAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    InfoRequest = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FirstReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReopenCount = table.Column<int>(type: "integer", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingReports", x => x.Id);
                    table.CheckConstraint("CK_ListingReports_ClosedHasOutcome", "(\"Status\" IN ('Resolved', 'Dismissed')) = (\"Outcome\" IS NOT NULL AND \"ResolvedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_ListingReports_NotOwnListing", "\"ReporterUserId\" <> \"ListingOwnerId\"");
                    table.CheckConstraint("CK_ListingReports_Priority", "\"Priority\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_ListingReports_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingReports_UserAccounts_AssignedAdminId",
                        column: x => x.AssignedAdminId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingReports_UserAccounts_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListingReportEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromValue = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsInternal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingReportEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingReportEvents_ListingReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "ListingReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReportEvents_ReportId_CreatedAt",
                table: "ListingReportEvents",
                columns: new[] { "ReportId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_AssignedAdminId_Status",
                table: "ListingReports",
                columns: new[] { "AssignedAdminId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ClientRequest",
                table: "ListingReports",
                columns: new[] { "ReporterUserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ListingOwnerId",
                table: "ListingReports",
                column: "ListingOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_Number",
                table: "ListingReports",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_OpenPerReporterAndListing",
                table: "ListingReports",
                columns: new[] { "ReporterUserId", "PropertyId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN ('New', 'UnderReview', 'WaitingForInformation', 'ActionRequired')");

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_Priority_CreatedAt",
                table: "ListingReports",
                columns: new[] { "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_PropertyId_CreatedAt",
                table: "ListingReports",
                columns: new[] { "PropertyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_ReporterUserId_CreatedAt",
                table: "ListingReports",
                columns: new[] { "ReporterUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingReports_Status_CreatedAt",
                table: "ListingReports",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingReportEvents");

            migrationBuilder.DropTable(
                name: "ListingReports");

            migrationBuilder.DropSequence(
                name: "ListingReportNumberSeq");
        }
    }
}
