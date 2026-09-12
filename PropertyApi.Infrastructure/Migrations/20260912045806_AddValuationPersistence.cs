using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddValuationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ValuationInquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: true),
                    PropertyTypeId = table.Column<int>(type: "integer", nullable: true),
                    Area = table.Column<decimal>(type: "numeric", nullable: true),
                    Rooms = table.Column<int>(type: "integer", nullable: true),
                    GovernorateId = table.Column<int>(type: "integer", nullable: false),
                    DistrictId = table.Column<int>(type: "integer", nullable: true),
                    NeighborhoodId = table.Column<int>(type: "integer", nullable: true),
                    RequestType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValuationInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValuationInquiries_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValuationInquiries_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValuationInquiries_Neighborhoods_NeighborhoodId",
                        column: x => x.NeighborhoodId,
                        principalTable: "Neighborhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValuationInquiries_UserAccounts_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValuationOfficeInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InquiryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchLevel = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValuationOfficeInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValuationOfficeInvitations_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValuationOfficeInvitations_ValuationInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "ValuationInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_DistrictId",
                table: "ValuationInquiries",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_GovernorateId",
                table: "ValuationInquiries",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_NeighborhoodId",
                table: "ValuationInquiries",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_RequesterId",
                table: "ValuationInquiries",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationInquiries_Status_ExpiresAt",
                table: "ValuationInquiries",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValuationOfficeInvitations_AgencyId",
                table: "ValuationOfficeInvitations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationOfficeInvitations_AgencyId_InquiryId",
                table: "ValuationOfficeInvitations",
                columns: new[] { "AgencyId", "InquiryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValuationOfficeInvitations_InquiryId",
                table: "ValuationOfficeInvitations",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationOfficeInvitations_Status",
                table: "ValuationOfficeInvitations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValuationOfficeInvitations");

            migrationBuilder.DropTable(
                name: "ValuationInquiries");
        }
    }
}
