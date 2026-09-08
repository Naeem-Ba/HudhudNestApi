using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingSurveysAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Campaign = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingEvents_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketingEvents_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SurveyResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: true),
                    WillingnessToPay = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PreferredPaymentModel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ExpectedMonthlyPriceUsd = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ExpectedPerListingPriceUsd = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    AcceptableCommissionPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    MostImportantFeature = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BiggestProblem = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SubscriptionBlocker = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WantsTrialBeforePaying = table.Column<bool>(type: "boolean", nullable: true),
                    TeamSize = table.Column<int>(type: "integer", nullable: true),
                    PropertyCount = table.Column<int>(type: "integer", nullable: true),
                    UsesSimilarToolCurrently = table.Column<bool>(type: "boolean", nullable: true),
                    SimilarToolName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyResponses_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_CreatedAt",
                table: "MarketingEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_EventType",
                table: "MarketingEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_EventType_CreatedAt",
                table: "MarketingEvents",
                columns: new[] { "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_LeadId",
                table: "MarketingEvents",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_OfferId",
                table: "MarketingEvents",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingEvents_SessionId",
                table: "MarketingEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_CreatedAt",
                table: "SurveyResponses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_LeadId",
                table: "SurveyResponses",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_PreferredPaymentModel",
                table: "SurveyResponses",
                column: "PreferredPaymentModel");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_WillingnessToPay",
                table: "SurveyResponses",
                column: "WillingnessToPay");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketingEvents");

            migrationBuilder.DropTable(
                name: "SurveyResponses");
        }
    }
}
