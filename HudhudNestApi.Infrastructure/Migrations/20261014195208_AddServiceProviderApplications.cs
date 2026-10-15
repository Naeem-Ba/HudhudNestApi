using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <summary>
    /// Service provider onboarding. New tables ServiceProviderApplications and
    /// ServiceProviderEvents (plus the SPA-number sequence); ServiceProviders gains Status,
    /// StatusReason, ApprovedCategories and CoverageGovernorateIds.
    ///
    /// Two raw-SQL backfills keep existing providers exactly as visible as before:
    /// Status follows the old IsActive flag, and ApprovedCategories is set to the categories the
    /// provider already has offerings in (the public catalog now requires an approved category).
    /// CoverageGovernorateIds stays empty for them, which means "not restricted".
    /// Down drops the new tables and columns; nothing that existed before is changed by Up.
    /// </summary>
    public partial class AddServiceProviderApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ServiceProviderApplicationNumberSeq");

            migrationBuilder.AddColumn<string[]>(
                name: "ApprovedCategories",
                table: "ServiceProviders",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<int[]>(
                name: "CoverageGovernorateIds",
                table: "ServiceProviders",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ServiceProviders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "ServiceProviders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ServiceProviders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ServiceProviderApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"ServiceProviderApplicationNumberSeq\"')"),
                    ApplicantUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequestedCategories = table.Column<string[]>(type: "text[]", nullable: false),
                    RequestedGovernorateIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    Experience = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InfoRequest = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ServiceProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TermsAcceptedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceProviderApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceProviderApplications_ServiceProviders_ServiceProvide~",
                        column: x => x.ServiceProviderId,
                        principalTable: "ServiceProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceProviderApplications_UserAccounts_ApplicantUserId",
                        column: x => x.ApplicantUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceProviderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsInternal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceProviderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceProviderEvents_ServiceProviderApplications_Applicati~",
                        column: x => x.ApplicationId,
                        principalTable: "ServiceProviderApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceProviderEvents_ServiceProviders_ServiceProviderId",
                        column: x => x.ServiceProviderId,
                        principalTable: "ServiceProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing providers: Status mirrors the old IsActive flag.
            migrationBuilder.Sql(
                "UPDATE \"ServiceProviders\" SET \"Status\" = 'Suspended' WHERE \"IsActive\" = false;");

            // Existing providers: approve the categories they already offer, so nothing they sell
            // disappears from the catalog when the approved-category rule takes effect.
            migrationBuilder.Sql(
                """
                UPDATE "ServiceProviders" AS sp
                SET "ApprovedCategories" = sub.categories
                FROM (
                    SELECT o."ServiceProviderId", array_agg(DISTINCT o."Category") AS categories
                    FROM "ServiceOfferings" AS o
                    WHERE o."IsDeleted" = false
                    GROUP BY o."ServiceProviderId"
                ) AS sub
                WHERE sub."ServiceProviderId" = sp."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceProviderApplications_Number",
                table: "ServiceProviderApplications",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceProviderApplications_OpenPerApplicant",
                table: "ServiceProviderApplications",
                column: "ApplicantUserId",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN ('Pending', 'NeedsInfo')");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceProviderApplications_ServiceProviderId",
                table: "ServiceProviderApplications",
                column: "ServiceProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceProviderApplications_Status_CreatedAt",
                table: "ServiceProviderApplications",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceProviderEvents_ApplicationId_CreatedAt",
                table: "ServiceProviderEvents",
                columns: new[] { "ApplicationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceProviderEvents_ServiceProviderId_CreatedAt",
                table: "ServiceProviderEvents",
                columns: new[] { "ServiceProviderId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceProviderEvents");

            migrationBuilder.DropTable(
                name: "ServiceProviderApplications");

            migrationBuilder.DropColumn(
                name: "ApprovedCategories",
                table: "ServiceProviders");

            migrationBuilder.DropColumn(
                name: "CoverageGovernorateIds",
                table: "ServiceProviders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ServiceProviders");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "ServiceProviders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ServiceProviders");

            migrationBuilder.DropSequence(
                name: "ServiceProviderApplicationNumberSeq");
        }
    }
}
