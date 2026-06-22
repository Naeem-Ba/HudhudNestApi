using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyrianMarketLookupsAndApplicationRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BanReason",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBanned",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "Users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppNumber",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Roles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Caption",
                table: "PropertyImages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageType",
                table: "PropertyImages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "PropertyImages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadedAt",
                table: "PropertyImages",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode",
                table: "Properties",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "SYP",
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3,
                oldDefaultValue: "EUR");

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "Properties",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                defaultValue: "SY",
                oldClrType: typeof(string),
                oldType: "character(2)",
                oldFixedLength: true,
                oldMaxLength: 2,
                oldDefaultValue: "DE");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "Properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuildingNumber",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DistrictId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ElectricityHoursPerDay",
                table: "Properties",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FavoritesCount",
                table: "Properties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeaturedUntil",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FurnishingStatus",
                table: "Properties",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unfurnished");

            migrationBuilder.AddColumn<string>(
                name: "GoogleMapsUrl",
                table: "Properties",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GovernorateId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasAC",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasElectricity",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGas",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGenerator",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasInternet",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLegalDispute",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSolarPanels",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasView",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasWater",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InternetType",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "KitchenCount",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalDisputeNotes",
                table: "Properties",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalStatus",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LegalStatusNotes",
                table: "Properties",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LivingRoomsCount",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NearestLandmark",
                table: "Properties",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NeighborhoodId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParkingCount",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceCurrencyId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyTypeId",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "Properties",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedByUserId",
                table: "Properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ViewDescription",
                table: "Properties",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ViewsCount",
                table: "Properties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "WaterDaysPerWeek",
                table: "Properties",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaterSource",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearBuilt",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoningNotes",
                table: "Properties",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoningStatus",
                table: "Properties",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExchangeRateToUSD = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ExchangeRateUpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsBaseCurrency = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DecimalPlaces = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Governorates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Governorates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertyReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyReviews_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PropertyReviews_Users_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropertyTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    AmountInUSD = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExchangeRateUsed = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TransactedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    VisitorPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VisitorNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProposedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    OwnerNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitRequests_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitRequests_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyRent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RentCurrencyId = table.Column<int>(type: "integer", nullable: false),
                    MinContractMonths = table.Column<int>(type: "integer", nullable: false),
                    MaxContractMonths = table.Column<int>(type: "integer", nullable: true),
                    SecurityDepositMonths = table.Column<int>(type: "integer", nullable: false),
                    SecurityDepositAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PaymentFrequency = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Monthly"),
                    AllowedTenantType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PetsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    SmokingAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    CommercialUseAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    IncludesWaterBill = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesElectricityBill = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesInternetBill = table.Column<bool>(type: "boolean", nullable: false),
                    RenewalPolicy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
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
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PriceCurrencyId = table.Column<int>(type: "integer", nullable: false),
                    IsPriceNegotiable = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Cash"),
                    InstallmentsYears = table.Column<int>(type: "integer", nullable: true),
                    DownPaymentPercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    DownPaymentAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MonthlyInstallment = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    InstallmentNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransferFeePercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    IncludesFurniture = table.Column<bool>(type: "boolean", nullable: false),
                    IncludesAppliances = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraInclusions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
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
                name: "Districts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GovernorateId = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Districts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Districts_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Neighborhoods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DistrictId = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Neighborhoods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Neighborhoods_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_AgentId",
                table: "Properties",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_DistrictId",
                table: "Properties",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_FurnishingStatus",
                table: "Properties",
                column: "FurnishingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_GovernorateId",
                table: "Properties",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_IsFeatured",
                table: "Properties",
                column: "IsFeatured");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_IsVerified",
                table: "Properties",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_LegalStatus",
                table: "Properties",
                column: "LegalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_NeighborhoodId",
                table: "Properties",
                column: "NeighborhoodId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_PriceCurrencyId",
                table: "Properties",
                column: "PriceCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_PropertyTypeId",
                table: "Properties",
                column: "PropertyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_Syrian_Search",
                table: "Properties",
                columns: new[] { "GovernorateId", "PropertyTypeId", "Status", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_IsActive",
                table: "Currencies",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_GovernorateId",
                table: "Districts",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_IsActive",
                table: "Districts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Governorates_CountryCode",
                table: "Governorates",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_Governorates_IsActive",
                table: "Governorates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Neighborhoods_DistrictId",
                table: "Neighborhoods",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Neighborhoods_IsActive",
                table: "Neighborhoods",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyReviews_PropertyId",
                table: "PropertyReviews",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyReviews_PropertyId_ReviewerId",
                table: "PropertyReviews",
                columns: new[] { "PropertyId", "ReviewerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyReviews_ReviewerId",
                table: "PropertyReviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyTypes_Category",
                table: "PropertyTypes",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyTypes_Code",
                table: "PropertyTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyTypes_IsActive",
                table: "PropertyTypes",
                column: "IsActive");

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
                name: "IX_Transactions_PayerId",
                table: "Transactions",
                column: "PayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PropertyId",
                table: "Transactions",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReceiverId",
                table: "Transactions",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReferenceNumber",
                table: "Transactions",
                column: "ReferenceNumber",
                unique: true,
                filter: "\"ReferenceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status",
                table: "Transactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactedAt",
                table: "Transactions",
                column: "TransactedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VisitRequests_PropertyId",
                table: "VisitRequests",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitRequests_PropertyId_RequesterId_Status",
                table: "VisitRequests",
                columns: new[] { "PropertyId", "RequesterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitRequests_RequesterId",
                table: "VisitRequests",
                column: "RequesterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Currencies_PriceCurrencyId",
                table: "Properties",
                column: "PriceCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Districts_DistrictId",
                table: "Properties",
                column: "DistrictId",
                principalTable: "Districts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Governorates_GovernorateId",
                table: "Properties",
                column: "GovernorateId",
                principalTable: "Governorates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Neighborhoods_NeighborhoodId",
                table: "Properties",
                column: "NeighborhoodId",
                principalTable: "Neighborhoods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_PropertyTypes_PropertyTypeId",
                table: "Properties",
                column: "PropertyTypeId",
                principalTable: "PropertyTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Users_AgentId",
                table: "Properties",
                column: "AgentId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Currencies_PriceCurrencyId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Districts_DistrictId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Governorates_GovernorateId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Neighborhoods_NeighborhoodId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_PropertyTypes_PropertyTypeId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Users_AgentId",
                table: "Properties");

            migrationBuilder.DropTable(
                name: "Neighborhoods");

            migrationBuilder.DropTable(
                name: "PropertyReviews");

            migrationBuilder.DropTable(
                name: "PropertyTypes");

            migrationBuilder.DropTable(
                name: "RentalDetails");

            migrationBuilder.DropTable(
                name: "SaleDetails");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "VisitRequests");

            migrationBuilder.DropTable(
                name: "Districts");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "Governorates");

            migrationBuilder.DropIndex(
                name: "IX_Properties_AgentId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_DistrictId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_FurnishingStatus",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_GovernorateId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_IsFeatured",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_IsVerified",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_LegalStatus",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_NeighborhoodId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_PriceCurrencyId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_PropertyTypeId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_Syrian_Search",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "BanReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsBanned",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WhatsAppNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Caption",
                table: "PropertyImages");

            migrationBuilder.DropColumn(
                name: "ImageType",
                table: "PropertyImages");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "PropertyImages");

            migrationBuilder.DropColumn(
                name: "UploadedAt",
                table: "PropertyImages");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "BuildingNumber",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "DistrictId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ElectricityHoursPerDay",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "FavoritesCount",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "FeaturedUntil",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "FurnishingStatus",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "GoogleMapsUrl",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "GovernorateId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasAC",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasElectricity",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasGas",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasGenerator",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasInternet",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasLegalDispute",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasSolarPanels",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasView",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "HasWater",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "InternetType",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "IsFeatured",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "KitchenCount",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "LegalDisputeNotes",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "LegalStatus",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "LegalStatusNotes",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "LivingRoomsCount",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "NearestLandmark",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "NeighborhoodId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ParkingCount",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "PriceCurrencyId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "PropertyTypeId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ViewDescription",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ViewsCount",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "WaterDaysPerWeek",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "WaterSource",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "YearBuilt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ZoningNotes",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ZoningStatus",
                table: "Properties");

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode",
                table: "Properties",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR",
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3,
                oldDefaultValue: "SYP");

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "Properties",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                defaultValue: "DE",
                oldClrType: typeof(string),
                oldType: "character(2)",
                oldFixedLength: true,
                oldMaxLength: 2,
                oldDefaultValue: "SY");
        }
    }
}
