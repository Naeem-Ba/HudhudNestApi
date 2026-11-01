using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertySearchRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertySearchRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GovernorateId = table.Column<int>(type: "integer", nullable: false),
                    DistrictId = table.Column<int>(type: "integer", nullable: true),
                    NeighborhoodId = table.Column<int>(type: "integer", nullable: true),
                    PropertyTypeId = table.Column<int>(type: "integer", nullable: true),
                    MinBudget = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    MaxBudget = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    MinRooms = table.Column<int>(type: "integer", nullable: true),
                    MinArea = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    MaxArea = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    NeedsBalcony = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsElevator = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsParking = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsFurnished = table.Column<bool>(type: "boolean", nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MatchedUpTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertySearchRequests", x => x.Id);
                    table.CheckConstraint("CK_PropertySearchRequests_Area", "\"MinArea\" IS NULL OR \"MaxArea\" IS NULL OR \"MinArea\" <= \"MaxArea\"");
                    table.CheckConstraint("CK_PropertySearchRequests_Budget", "\"MaxBudget\" > 0 AND (\"MinBudget\" IS NULL OR \"MinBudget\" <= \"MaxBudget\")");
                    table.ForeignKey(
                        name: "FK_PropertySearchRequests_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropertySearchRequests_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropertySearchRequests_Neighborhoods_NeighborhoodId",
                        column: x => x.NeighborhoodId,
                        principalTable: "Neighborhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropertySearchRequests_PropertyTypes_PropertyTypeId",
                        column: x => x.PropertyTypeId,
                        principalTable: "PropertyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropertySearchRequests_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchRequestMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SearchRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Reasons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RequesterNotifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OwnerNotifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchRequestMatches", x => x.Id);
                    table.CheckConstraint("CK_SearchRequestMatches_Score", "\"Score\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_SearchRequestMatches_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SearchRequestMatches_PropertySearchRequests_SearchRequestId",
                        column: x => x.SearchRequestId,
                        principalTable: "PropertySearchRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SearchRequestMatches_UserAccounts_ProposedByUserId",
                        column: x => x.ProposedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertySearchRequests_Active",
                table: "PropertySearchRequests",
                columns: new[] { "GovernorateId", "ListingType", "ExpiresAt" },
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_PropertySearchRequests_DistrictId",
                table: "PropertySearchRequests",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertySearchRequests_NeighborhoodId",
                table: "PropertySearchRequests",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertySearchRequests_PropertyTypeId",
                table: "PropertySearchRequests",
                column: "PropertyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertySearchRequests_UserId_Status",
                table: "PropertySearchRequests",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchRequestMatches_PropertyId",
                table: "SearchRequestMatches",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchRequestMatches_ProposedByUserId_ProposedAt",
                table: "SearchRequestMatches",
                columns: new[] { "ProposedByUserId", "ProposedAt" },
                filter: "\"ProposedByUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SearchRequestMatches_SearchRequestId_PropertyId",
                table: "SearchRequestMatches",
                columns: new[] { "SearchRequestId", "PropertyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchRequestMatches");

            migrationBuilder.DropTable(
                name: "PropertySearchRequests");
        }
    }
}
