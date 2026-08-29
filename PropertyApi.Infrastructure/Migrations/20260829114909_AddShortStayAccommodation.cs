using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShortStayAccommodation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccommodationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccommodationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostVerificationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    VerifiedByAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostVerificationRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShortStayListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccommodationTypeId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    Bedrooms = table.Column<int>(type: "integer", nullable: false),
                    Bathrooms = table.Column<int>(type: "integer", nullable: false),
                    CheckInTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CheckOutTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    SelfCheckInEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    InstantBookingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequestBookingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    GovernorateId = table.Column<int>(type: "integer", nullable: true),
                    DistrictId = table.Column<int>(type: "integer", nullable: true),
                    NeighborhoodId = table.Column<int>(type: "integer", nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LocationVisibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PoolType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PoolLocation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PoolIsSeasonal = table.Column<bool>(type: "boolean", nullable: true),
                    PoolIsHeated = table.Column<bool>(type: "boolean", nullable: true),
                    CleaningFee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExtraGuestFee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExtraBedFee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AllowsSmoking = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsParties = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsPets = table.Column<bool>(type: "boolean", nullable: false),
                    QuietHoursStart = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    QuietHoursEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CustomRulesText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CancellationFreeCancellationDays = table.Column<int>(type: "integer", nullable: false),
                    CancellationDepositRefundable = table.Column<bool>(type: "boolean", nullable: false),
                    CancellationCustomTermsText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DepositPercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_ShortStayListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayListings_AccommodationTypes_AccommodationTypeId",
                        column: x => x.AccommodationTypeId,
                        principalTable: "AccommodationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayListingAmenities",
                columns: table => new
                {
                    ShortStayListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayListingAmenities", x => new { x.ShortStayListingId, x.AmenityId });
                    table.ForeignKey(
                        name: "FK_ShortStayListingAmenities_Amenities_AmenityId",
                        column: x => x.AmenityId,
                        principalTable: "Amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShortStayListingAmenities_ShortStayListings_ShortStayListin~",
                        column: x => x.ShortStayListingId,
                        principalTable: "ShortStayListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayListingPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PublicId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AltText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ShortStayListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayListingPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayListingPhotos_ShortStayListings_ShortStayListingId",
                        column: x => x.ShortStayListingId,
                        principalTable: "ShortStayListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayRoomTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShortStayListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BasePricePerNight = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayRoomTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayRoomTypes_ShortStayListings_ShortStayListingId",
                        column: x => x.ShortStayListingId,
                        principalTable: "ShortStayListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccommodationUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccommodationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccommodationUnits_ShortStayRoomTypes_RoomTypeId",
                        column: x => x.RoomTypeId,
                        principalTable: "ShortStayRoomTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayMinimumStayRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumNights = table.Column<int>(type: "integer", nullable: false),
                    DateRangeStart = table.Column<DateOnly>(type: "date", nullable: true),
                    DateRangeEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayMinimumStayRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayMinimumStayRules_ShortStayRoomTypes_RoomTypeId",
                        column: x => x.RoomTypeId,
                        principalTable: "ShortStayRoomTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayPricingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: true),
                    DateRangeStart = table.Column<DateOnly>(type: "date", nullable: true),
                    DateRangeEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PricePerNight = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayPricingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayPricingRules_ShortStayRoomTypes_RoomTypeId",
                        column: x => x.RoomTypeId,
                        principalTable: "ShortStayRoomTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShortStayBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckIn = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOut = table.Column<DateOnly>(type: "date", nullable: false),
                    Adults = table.Column<int>(type: "integer", nullable: false),
                    Children = table.Column<int>(type: "integer", nullable: false),
                    Infants = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CancellationPolicyFreeCancellationDays = table.Column<int>(type: "integer", nullable: false),
                    CancellationPolicyDepositRefundable = table.Column<bool>(type: "boolean", nullable: false),
                    CancellationPolicyCustomTermsText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    HouseRulesSnapshotText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HouseRulesAcceptedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HostNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortStayBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortStayBookings_AccommodationUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "AccommodationUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitBookingRanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckIn = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOut = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitBookingRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitBookingRanges_AccommodationUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "AccommodationUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnitBookingRanges_ShortStayBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "ShortStayBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccommodationTypes_Code",
                table: "AccommodationTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccommodationTypes_IsActive",
                table: "AccommodationTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AccommodationUnits_RoomTypeId",
                table: "AccommodationUnits",
                column: "RoomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_HostVerificationRecords_UserId_Type",
                table: "HostVerificationRecords",
                columns: new[] { "UserId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayBookings_GuestId",
                table: "ShortStayBookings",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayBookings_Status",
                table: "ShortStayBookings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayBookings_UnitId",
                table: "ShortStayBookings",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListingAmenities_AmenityId",
                table: "ShortStayListingAmenities",
                column: "AmenityId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListingPhotos_ShortStayListingId",
                table: "ShortStayListingPhotos",
                column: "ShortStayListingId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_AccommodationTypeId",
                table: "ShortStayListings",
                column: "AccommodationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_City",
                table: "ShortStayListings",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_IsPublished",
                table: "ShortStayListings",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayListings_OwnerId",
                table: "ShortStayListings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayMinimumStayRules_RoomTypeId",
                table: "ShortStayMinimumStayRules",
                column: "RoomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayPricingRules_RoomTypeId",
                table: "ShortStayPricingRules",
                column: "RoomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayReviews_BookingId",
                table: "ShortStayReviews",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayReviews_ShortStayListingId",
                table: "ShortStayReviews",
                column: "ShortStayListingId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortStayRoomTypes_ShortStayListingId",
                table: "ShortStayRoomTypes",
                column: "ShortStayListingId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitBookingRanges_BookingId",
                table: "UnitBookingRanges",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitBookingRanges_UnitId",
                table: "UnitBookingRanges",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitBookingRanges_UnitId_CheckIn_CheckOut",
                table: "UnitBookingRanges",
                columns: new[] { "UnitId", "CheckIn", "CheckOut" });

            // btree_gist lets a GiST exclusion index compare the plain UUID "UnitId" column
            // with "=" alongside the daterange "&&" (overlap) operator in one constraint.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
            """);

            // The real, unconditional double-booking guarantee (plan's "قرارات معمارية حاسمة").
            // Two Reserved/CheckedIn ranges for the SAME unit can never overlap, enforced by
            // Postgres itself — CreateBookingCommandHandler's advisory-lock + pre-check
            // (BookingRepository.HasOverlappingReservationAsync) is the first line of defense,
            // this is the backstop that holds even if that check is ever bypassed. Pending and
            // Blocked/Released ranges are excluded from the constraint on purpose: several
            // Pending requests may legitimately compete for the same dates (the host picks one -
            // see ApproveBookingCommandHandler), and a Released/Blocked range must not conflict
            // with a later real booking of the same dates.
            migrationBuilder.Sql("""
                ALTER TABLE "UnitBookingRanges"
                ADD CONSTRAINT "EX_UnitBookingRanges_NoOverlap"
                EXCLUDE USING gist (
                    "UnitId" WITH =,
                    daterange("CheckIn", "CheckOut", '[)') WITH &&
                )
                WHERE ("Status" IN ('Reserved', 'CheckedIn'));
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostVerificationRecords");

            migrationBuilder.DropTable(
                name: "ShortStayListingAmenities");

            migrationBuilder.DropTable(
                name: "ShortStayListingPhotos");

            migrationBuilder.DropTable(
                name: "ShortStayMinimumStayRules");

            migrationBuilder.DropTable(
                name: "ShortStayPricingRules");

            migrationBuilder.DropTable(
                name: "ShortStayReviews");

            migrationBuilder.DropTable(
                name: "UnitBookingRanges");

            migrationBuilder.DropTable(
                name: "ShortStayBookings");

            migrationBuilder.DropTable(
                name: "AccommodationUnits");

            migrationBuilder.DropTable(
                name: "ShortStayRoomTypes");

            migrationBuilder.DropTable(
                name: "ShortStayListings");

            migrationBuilder.DropTable(
                name: "AccommodationTypes");
        }
    }
}
