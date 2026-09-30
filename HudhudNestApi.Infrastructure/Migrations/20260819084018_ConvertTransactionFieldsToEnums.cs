using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HudhudNestApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConvertTransactionFieldsToEnums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TransactionType",
                table: "Transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Pending");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "Transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConfirmedAvailableAt",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PropertyPriceHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceField = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OldValue = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    NewValue = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyPriceHistories_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    City = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Region = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    GovernorateId = table.Column<int>(type: "integer", nullable: true),
                    DistrictId = table.Column<int>(type: "integer", nullable: true),
                    NeighborhoodId = table.Column<int>(type: "integer", nullable: true),
                    PropertyTypeId = table.Column<int>(type: "integer", nullable: true),
                    ListingType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MinPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    MaxPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    MinRooms = table.Column<int>(type: "integer", nullable: true),
                    MaxRooms = table.Column<int>(type: "integer", nullable: true),
                    MinArea = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    MaxArea = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    LastMatchedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedSearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedSearches_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_LastConfirmedAvailableAt",
                table: "Properties",
                column: "LastConfirmedAvailableAt");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPriceHistories_PropertyId_ChangedAt",
                table: "PropertyPriceHistories",
                columns: new[] { "PropertyId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_LastMatchedAt",
                table: "SavedSearches",
                column: "LastMatchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_UserId",
                table: "SavedSearches",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PropertyPriceHistories");

            migrationBuilder.DropTable(
                name: "SavedSearches");

            migrationBuilder.DropIndex(
                name: "IX_Properties_LastConfirmedAvailableAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "LastConfirmedAvailableAt",
                table: "Properties");

            migrationBuilder.AlterColumn<string>(
                name: "TransactionType",
                table: "Transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "Transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
