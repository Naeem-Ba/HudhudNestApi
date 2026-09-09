using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialQueueAndAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SocialMediaAssetId",
                table: "SocialPostContents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SocialMediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialMediaAssets_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SocialMediaAssets_SocialPublications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "SocialPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocialPublicationDeadLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SocialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FailedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPublicationDeadLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPublicationDeadLetters_SocialPublications_Publication~",
                        column: x => x.PublicationId,
                        principalTable: "SocialPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostContents_SocialMediaAssetId",
                table: "SocialPostContents",
                column: "SocialMediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaAssets_Checksum",
                table: "SocialMediaAssets",
                column: "Checksum");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaAssets_PropertyId",
                table: "SocialMediaAssets",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaAssets_PropertyId_Platform_AssetType_TemplateId_~",
                table: "SocialMediaAssets",
                columns: new[] { "PropertyId", "Platform", "AssetType", "TemplateId", "TemplateVersion", "Checksum" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaAssets_PublicationId",
                table: "SocialMediaAssets",
                column: "PublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMediaAssets_TemplateId",
                table: "SocialMediaAssets",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublicationDeadLetters_FailedAt",
                table: "SocialPublicationDeadLetters",
                column: "FailedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublicationDeadLetters_PublicationId",
                table: "SocialPublicationDeadLetters",
                column: "PublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPublicationDeadLetters_ResolvedAt",
                table: "SocialPublicationDeadLetters",
                column: "ResolvedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPostContents_SocialMediaAssets_SocialMediaAssetId",
                table: "SocialPostContents",
                column: "SocialMediaAssetId",
                principalTable: "SocialMediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPostContents_SocialMediaAssets_SocialMediaAssetId",
                table: "SocialPostContents");

            migrationBuilder.DropTable(
                name: "SocialMediaAssets");

            migrationBuilder.DropTable(
                name: "SocialPublicationDeadLetters");

            migrationBuilder.DropIndex(
                name: "IX_SocialPostContents_SocialMediaAssetId",
                table: "SocialPostContents");

            migrationBuilder.DropColumn(
                name: "SocialMediaAssetId",
                table: "SocialPostContents");
        }
    }
}
