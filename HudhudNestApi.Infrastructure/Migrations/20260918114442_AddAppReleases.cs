using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MinimumSupportedVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StoreUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ReleaseNotesAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReleaseNotesEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReleaseNotesDe = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppReleases_Platform_IsEnabled",
                table: "AppReleases",
                columns: new[] { "Platform", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AppReleases_Platform_Version",
                table: "AppReleases",
                columns: new[] { "Platform", "Version" },
                unique: true,
                filter: "\"IsEnabled\" = true AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppReleases");
        }
    }
}
