using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyAndFeaturedListingPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                table: "UserAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgencyJoinedAt",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                table: "Properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LogoPublicId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    LicenseNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_Agencies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_AgencyId",
                table: "UserAccounts",
                column: "AgencyId",
                filter: "\"AgencyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_AgencyId",
                table: "Properties",
                column: "AgencyId",
                filter: "\"AgencyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_FeaturedUntil_Active",
                table: "Properties",
                column: "FeaturedUntil",
                filter: "\"IsFeatured\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_CountryCode_City",
                table: "Agencies",
                columns: new[] { "CountryCode", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_OwnerUserId",
                table: "Agencies",
                column: "OwnerUserId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_Slug",
                table: "Agencies",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Agencies_AgencyId",
                table: "Properties",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_Agencies_AgencyId",
                table: "UserAccounts",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Agencies_AgencyId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_Agencies_AgencyId",
                table: "UserAccounts");

            migrationBuilder.DropTable(
                name: "Agencies");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_AgencyId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Properties_AgencyId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_FeaturedUntil_Active",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "AgencyJoinedAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "Properties");
        }
    }
}
