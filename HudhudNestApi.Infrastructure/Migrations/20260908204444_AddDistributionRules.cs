using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DistributionRuleId",
                table: "SocialPublications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DistributionRunId",
                table: "SocialPublications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DistributionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProvinceId = table.Column<int>(type: "integer", nullable: true),
                    PropertyTypeId = table.Column<int>(type: "integer", nullable: true),
                    TransactionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SocialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EndAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_DistributionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistributionRules_Governorates_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DistributionRules_PropertyTypes_PropertyTypeId",
                        column: x => x.PropertyTypeId,
                        principalTable: "PropertyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DistributionRules_SocialAccounts_SocialAccountId",
                        column: x => x.SocialAccountId,
                        principalTable: "SocialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DistributionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MatchedRuleCount = table.Column<int>(type: "integer", nullable: false),
                    PublicationsCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    ResultReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_DistributionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DistributionRuns_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_DistributionRuleId",
                table: "SocialPublications",
                column: "DistributionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_DistributionRunId",
                table: "SocialPublications",
                column: "DistributionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_PropertyId_SocialAccountId",
                table: "SocialPublications",
                columns: new[] { "PropertyId", "SocialAccountId" },
                unique: true,
                filter: "\"DistributionRuleId\" IS NOT NULL AND \"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_IsActive",
                table: "DistributionRules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_IsActive_IsArchived",
                table: "DistributionRules",
                columns: new[] { "IsActive", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_Priority",
                table: "DistributionRules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_PropertyTypeId",
                table: "DistributionRules",
                column: "PropertyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_ProvinceId",
                table: "DistributionRules",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_SocialAccountId",
                table: "DistributionRules",
                column: "SocialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRules_TransactionType",
                table: "DistributionRules",
                column: "TransactionType");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRuns_CreatedAt",
                table: "DistributionRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRuns_PropertyId",
                table: "DistributionRuns",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRuns_Status",
                table: "DistributionRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionRuns_TriggerType",
                table: "DistributionRuns",
                column: "TriggerType");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPublications_DistributionRules_DistributionRuleId",
                table: "SocialPublications",
                column: "DistributionRuleId",
                principalTable: "DistributionRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPublications_DistributionRuns_DistributionRunId",
                table: "SocialPublications",
                column: "DistributionRunId",
                principalTable: "DistributionRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPublications_DistributionRules_DistributionRuleId",
                table: "SocialPublications");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialPublications_DistributionRuns_DistributionRunId",
                table: "SocialPublications");

            migrationBuilder.DropTable(
                name: "DistributionRules");

            migrationBuilder.DropTable(
                name: "DistributionRuns");

            migrationBuilder.DropIndex(
                name: "IX_SocialPublications_DistributionRuleId",
                table: "SocialPublications");

            migrationBuilder.DropIndex(
                name: "IX_SocialPublications_DistributionRunId",
                table: "SocialPublications");

            migrationBuilder.DropIndex(
                name: "IX_SocialPublications_PropertyId_SocialAccountId",
                table: "SocialPublications");

            migrationBuilder.DropColumn(
                name: "DistributionRuleId",
                table: "SocialPublications");

            migrationBuilder.DropColumn(
                name: "DistributionRunId",
                table: "SocialPublications");
        }
    }
}
