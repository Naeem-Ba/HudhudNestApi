using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentDiscoveryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestmentProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    ProjectType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RiskLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TargetAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    MinimumInvestment = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    MaximumInvestment = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    InvestmentTermMonths = table.Column<int>(type: "integer", nullable: false),
                    ExpectedReturnMin = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    ExpectedReturnMax = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    RaisedAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScheduledPublishAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_InvestmentProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentProjects_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentProjects_UserAccounts_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StorageProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DocumentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_InvestmentDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentDocuments_InvestmentProjects_InvestmentProjectId",
                        column: x => x.InvestmentProjectId,
                        principalTable: "InvestmentProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentInterests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentInterests_InvestmentProjects_InvestmentProjectId",
                        column: x => x.InvestmentProjectId,
                        principalTable: "InvestmentProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvestmentInterests_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentProjectFinancials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    RenovationCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ConstructionCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Taxes = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    NotaryCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    BrokerCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    FinancingCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    OperatingCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ContingencyReserve = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpectedRevenue = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpectedProfit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TotalProjectCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentProjectFinancials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentProjectFinancials_InvestmentProjects_InvestmentPr~",
                        column: x => x.InvestmentProjectId,
                        principalTable: "InvestmentProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentRiskAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MarketRisk = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LiquidityRisk = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProjectRisk = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FinancingRisk = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeveloperRisk = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RiskScore = table.Column<int>(type: "integer", nullable: false),
                    RiskSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentRiskAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentRiskAssessments_InvestmentProjects_InvestmentProj~",
                        column: x => x.InvestmentProjectId,
                        principalTable: "InvestmentProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    UpdateType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_InvestmentUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentUpdates_InvestmentProjects_InvestmentProjectId",
                        column: x => x.InvestmentProjectId,
                        principalTable: "InvestmentProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentWatchlistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentWatchlistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentWatchlistItems_InvestmentProjects_InvestmentProje~",
                        column: x => x.InvestmentProjectId,
                        principalTable: "InvestmentProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvestmentWatchlistItems_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentDocuments_InvestmentProjectId",
                table: "InvestmentDocuments",
                column: "InvestmentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentDocuments_InvestmentProjectId_IsPublic",
                table: "InvestmentDocuments",
                columns: new[] { "InvestmentProjectId", "IsPublic" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentInterests_InvestmentProjectId",
                table: "InvestmentInterests",
                column: "InvestmentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentInterests_UserId",
                table: "InvestmentInterests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentInterests_UserId_InvestmentProjectId",
                table: "InvestmentInterests",
                columns: new[] { "UserId", "InvestmentProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjectFinancials_InvestmentProjectId",
                table: "InvestmentProjectFinancials",
                column: "InvestmentProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjects_EndDate",
                table: "InvestmentProjects",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjects_OwnerUserId",
                table: "InvestmentProjects",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjects_ProjectType",
                table: "InvestmentProjects",
                column: "ProjectType");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjects_PropertyId",
                table: "InvestmentProjects",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjects_StartDate",
                table: "InvestmentProjects",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProjects_Status",
                table: "InvestmentProjects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentRiskAssessments_InvestmentProjectId",
                table: "InvestmentRiskAssessments",
                column: "InvestmentProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentUpdates_InvestmentProjectId_PublishedAt",
                table: "InvestmentUpdates",
                columns: new[] { "InvestmentProjectId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentWatchlistItems_InvestmentProjectId",
                table: "InvestmentWatchlistItems",
                column: "InvestmentProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentWatchlistItems_UserId",
                table: "InvestmentWatchlistItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentWatchlistItems_UserId_InvestmentProjectId",
                table: "InvestmentWatchlistItems",
                columns: new[] { "UserId", "InvestmentProjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentDocuments");

            migrationBuilder.DropTable(
                name: "InvestmentInterests");

            migrationBuilder.DropTable(
                name: "InvestmentProjectFinancials");

            migrationBuilder.DropTable(
                name: "InvestmentRiskAssessments");

            migrationBuilder.DropTable(
                name: "InvestmentUpdates");

            migrationBuilder.DropTable(
                name: "InvestmentWatchlistItems");

            migrationBuilder.DropTable(
                name: "InvestmentProjects");
        }
    }
}
