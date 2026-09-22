using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountsProfileProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    TaxNumber = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    WhatsAppNumber = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PreferredCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });
            migrationBuilder.Sql(
    """
    INSERT INTO "UserAccounts"
    (
        "Id",
        "FirstName",
        "LastName",
        "DisplayName",
        "TaxNumber",
        "ProfileImageUrl",
        "WhatsAppNumber",
        "PreferredLanguage",
        "PreferredCurrency",
        "CountryCode",
        "CreatedAt",
        "UpdatedAt"
    )
    SELECT
        "Id",
        "FirstName",
        "LastName",
        "DisplayName",
        "TaxNumber",
        "ProfileImageUrl",
        "WhatsAppNumber",
        "PreferredLanguage",
        "PreferredCurrency",
        "CountryCode",
        "CreatedAt",
        "UpdatedAt"
    FROM "Users";
    """);

            migrationBuilder.Sql(
    """
    DO $$
    BEGIN
        IF
        (
            SELECT COUNT(*)
            FROM "UserAccounts"
        ) <>
        (
            SELECT COUNT(*)
            FROM "Users"
        )
        THEN
            RAISE EXCEPTION
                'UserAccounts backfill validation failed: row counts do not match.';
        END IF;
    END
    $$;
    """);


            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_CountryCode",
                table: "UserAccounts",
                column: "CountryCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAccounts");
        }
    }
}
