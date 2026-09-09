using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConfigurationVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GovernorateId = table.Column<int>(type: "integer", nullable: true),
                    CredentialReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_SocialAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialAccounts_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SocialAccounts_SocialChannels_SocialChannelId",
                        column: x => x.SocialChannelId,
                        principalTable: "SocialChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocialPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExternalPostId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalPostUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    MaxRetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastRetryAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UtmSource = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UtmMedium = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UtmCampaign = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UtmContent = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
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
                    table.PrimaryKey("PK_SocialPublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPublications_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SocialPublications_SocialAccounts_SocialAccountId",
                        column: x => x.SocialAccountId,
                        principalTable: "SocialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocialPostContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Hashtags = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPostContents_SocialPublications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "SocialPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialPublicationStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialPublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPublicationStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPublicationStatusHistories_SocialPublications_SocialP~",
                        column: x => x.SocialPublicationId,
                        principalTable: "SocialPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_GovernorateId",
                table: "SocialAccounts",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_Platform_ExternalAccountId",
                table: "SocialAccounts",
                columns: new[] { "Platform", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_SocialChannelId",
                table: "SocialAccounts",
                column: "SocialChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_Status",
                table: "SocialAccounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SocialChannels_Platform",
                table: "SocialChannels",
                column: "Platform",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostContents_PublicationId",
                table: "SocialPostContents",
                column: "PublicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_CreatedAt",
                table: "SocialPublications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_NextRetryAt",
                table: "SocialPublications",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_PropertyId",
                table: "SocialPublications",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_ScheduledAt",
                table: "SocialPublications",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_SocialAccountId",
                table: "SocialPublications",
                column: "SocialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_Status",
                table: "SocialPublications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_UtmCampaign",
                table: "SocialPublications",
                column: "UtmCampaign");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublications_UtmSource",
                table: "SocialPublications",
                column: "UtmSource");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublicationStatusHistories_CreatedAt",
                table: "SocialPublicationStatusHistories",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublicationStatusHistories_SocialPublicationId",
                table: "SocialPublicationStatusHistories",
                column: "SocialPublicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPostContents");

            migrationBuilder.DropTable(
                name: "SocialPublicationStatusHistories");

            migrationBuilder.DropTable(
                name: "SocialPublications");

            migrationBuilder.DropTable(
                name: "SocialAccounts");

            migrationBuilder.DropTable(
                name: "SocialChannels");
        }
    }
}
