using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptionBAuditCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Currencies_PriceCurrencyId",
                table: "Properties");

            migrationBuilder.DropTable(
                name: "OtpCodes");

            migrationBuilder.DropTable(
                name: "RentalDetails");

            migrationBuilder.DropTable(
                name: "SaleDetails");

            migrationBuilder.DropTable(
                name: "ServiceReviewDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Properties_PriceCurrencyId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "PriceCurrencyId",
                table: "Properties");

            migrationBuilder.CreateTable(
                name: "ServiceRequestDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FilePublicId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FileType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequestDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequestDocuments_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BookingId",
                table: "Transactions",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneOtpChallenges_UserId",
                table: "PhoneOtpChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequestDocuments_ServiceRequestId",
                table: "ServiceRequestDocuments",
                column: "ServiceRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agencies_UserAccounts_OwnerUserId",
                table: "Agencies",
                column: "OwnerUserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PhoneOtpChallenges_Users_UserId",
                table: "PhoneOtpChallenges",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceProviders_UserAccounts_UserId",
                table: "ServiceProviders",
                column: "UserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Properties_PropertyId",
                table: "Transactions",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_UserAccounts_PayerId",
                table: "Transactions",
                column: "PayerId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_UserAccounts_ReceiverId",
                table: "Transactions",
                column: "ReceiverId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_VisitRequests_BookingId",
                table: "Transactions",
                column: "BookingId",
                principalTable: "VisitRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_Users_Id",
                table: "UserAccounts",
                column: "Id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agencies_UserAccounts_OwnerUserId",
                table: "Agencies");

            migrationBuilder.DropForeignKey(
                name: "FK_PhoneOtpChallenges_Users_UserId",
                table: "PhoneOtpChallenges");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceProviders_UserAccounts_UserId",
                table: "ServiceProviders");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Properties_PropertyId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_UserAccounts_PayerId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_UserAccounts_ReceiverId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_VisitRequests_BookingId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_Users_Id",
                table: "UserAccounts");

            migrationBuilder.DropTable(
                name: "ServiceRequestDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_BookingId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_PhoneOtpChallenges_UserId",
                table: "PhoneOtpChallenges");

            migrationBuilder.AddColumn<int>(
                name: "PriceCurrencyId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OtpCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RentalDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentCurrencyId = table.Column<int>(type: "integer", nullable: false),
                    AllowedTenantType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AvailableFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    CommercialUseAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesElectricityBill = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesInternetBill = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesWaterBill = table.Column<bool>(type: "boolean", nullable: false),
                    MaxContractMonths = table.Column<int>(type: "integer", nullable: true),
                    MinContractMonths = table.Column<int>(type: "integer", nullable: false),
                    MonthlyRent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentFrequency = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Monthly"),
                    PetsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    RenewalPolicy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SecurityDepositAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    SecurityDepositMonths = table.Column<int>(type: "integer", nullable: false),
                    SmokingAllowed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RentalDetails_Currencies_RentCurrencyId",
                        column: x => x.RentCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RentalDetails_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PriceCurrencyId = table.Column<int>(type: "integer", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DownPaymentAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DownPaymentPercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ExtraInclusions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IncludesAppliances = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesFurniture = table.Column<bool>(type: "boolean", nullable: false),
                    InstallmentNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InstallmentsYears = table.Column<int>(type: "integer", nullable: true),
                    IsPriceNegotiable = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyInstallment = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Cash"),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TransferFeePercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleDetails_Currencies_PriceCurrencyId",
                        column: x => x.PriceCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleDetails_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceReviewDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FilePublicId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FileType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceReviewDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceReviewDocuments_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_PriceCurrencyId",
                table: "Properties",
                column: "PriceCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_ExpiresAt",
                table: "OtpCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Phone_CreatedAt",
                table: "OtpCodes",
                columns: new[] { "PhoneNumber", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpCodes_Phone_Purpose_ExpiresAt",
                table: "OtpCodes",
                columns: new[] { "PhoneNumber", "Purpose", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RentalDetails_PropertyId",
                table: "RentalDetails",
                column: "PropertyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RentalDetails_RentCurrencyId",
                table: "RentalDetails",
                column: "RentCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleDetails_PriceCurrencyId",
                table: "SaleDetails",
                column: "PriceCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleDetails_PropertyId",
                table: "SaleDetails",
                column: "PropertyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviewDocuments_ServiceRequestId",
                table: "ServiceReviewDocuments",
                column: "ServiceRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Currencies_PriceCurrencyId",
                table: "Properties",
                column: "PriceCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
