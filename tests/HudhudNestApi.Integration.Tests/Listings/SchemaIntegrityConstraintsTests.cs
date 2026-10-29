using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Pins the schema audit of 2026-10-29 (migration NormalizeSchemaIntegrity): the new foreign keys
/// and checks exist and are validated on a clean database, the redundant indexes are gone, and the
/// database itself now refuses rows the domain already refused.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class SchemaIntegrityConstraintsTests : IAsyncLifetime
{
    private static readonly string[] NewConstraints =
    [
        "FK_ShortStayListings_UserAccounts_OwnerId",
        "FK_ShortStayListings_Properties_PropertyId",
        "FK_ShortStayListings_Governorates_GovernorateId",
        "FK_ShortStayListings_Districts_DistrictId",
        "FK_ShortStayListings_Neighborhoods_NeighborhoodId",
        "FK_ShortStayBookings_UserAccounts_GuestId",
        "FK_ShortStayReviews_ShortStayBookings_BookingId",
        "FK_ShortStayReviews_UserAccounts_ReviewerId",
        "FK_ShortStayReviews_ShortStayListings_ShortStayListingId",
        "FK_HostVerificationRecords_UserAccounts_UserId",
        "FK_ServiceReviews_UserAccounts_ReviewerId",
        "FK_ServiceProviders_Agencies_AgencyId",
        "FK_Transactions_Currencies_CurrencyId",
        "FK_ValuationInquiries_PropertyTypes_PropertyTypeId",
        "FK_SavedSearches_Governorates_GovernorateId",
        "FK_SavedSearches_Districts_DistrictId",
        "FK_SavedSearches_Neighborhoods_NeighborhoodId",
        "FK_SavedSearches_PropertyTypes_PropertyTypeId",
        "CK_ShortStayBookings_Dates",
        "CK_ShortStayBookings_Amounts",
        "CK_ShortStayReviews_Rating",
        "CK_PropertyReviews_Rating",
        "CK_ServiceReviews_Rating",
        "CK_UserRatings_InformationAccuracy",
        "CK_UserRatings_Credibility",
        "CK_UserRatings_Safety",
        "CK_UserRatings_ResponseSpeed",
        "CK_UserRatings_Transparency",
        "CK_UserRatings_Conduct",
    ];

    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Every new key and check exists and is validated on a clean database")]
    public async Task NewConstraints_ExistAndAreValidated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var validated = await db.Database
            .SqlQueryRaw<string>(
                "SELECT conname::text AS \"Value\" FROM pg_constraint " +
                "WHERE connamespace = 'public'::regnamespace AND convalidated")
            .ToListAsync();

        Assert.Empty(NewConstraints.Except(validated));
    }

    [Fact(DisplayName = "Indexes that only repeated the start of a wider index are gone")]
    public async Task RedundantIndexes_AreDropped()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var names = await db.Database
            .SqlQueryRaw<string>(
                "SELECT indexname::text AS \"Value\" FROM pg_indexes WHERE indexname IN " +
                "('IX_Properties_Status', 'IX_Properties_CountryCode', 'IX_Properties_GovernorateId', " +
                "'IX_Favorites_UserId', 'IX_RefreshTokens_UserId', 'IX_UnitBookingRanges_UnitId', " +
                "'IX_Properties_Search', 'IX_Properties_Syrian_Search', 'PK_Favorites')")
            .ToListAsync();

        // The wider index that covers each dropped one is still there.
        Assert.Equal(
            ["IX_Properties_Search", "IX_Properties_Syrian_Search", "PK_Favorites"],
            names.Order().ToArray());
    }

    [Fact(DisplayName = "A short-stay listing whose owner does not exist is refused by the database")]
    public async Task OrphanShortStayOwner_IsRefused()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The test database is truncated between classes, so the listing's accommodation type is
        // created in the same statement; only the owner is missing.
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "WITH t AS (INSERT INTO \"AccommodationTypes\" (\"Code\", \"NameAr\", \"NameEn\", \"Category\", \"Icon\", \"IsActive\", \"SortOrder\") " +
            "VALUES (left('schema-audit-' || gen_random_uuid(), 50), 'x', 'x', 'Apartment', 'x', true, 0) RETURNING \"Id\") " +
            "INSERT INTO \"ShortStayListings\" (\"Id\", \"OwnerId\", \"AccommodationTypeId\", \"Title\", \"Description\", " +
            "\"Capacity\", \"Bedrooms\", \"Bathrooms\", \"CheckInTime\", \"CheckOutTime\", \"SelfCheckInEnabled\", " +
            "\"InstantBookingEnabled\", \"RequestBookingEnabled\", \"LocationVisibility\", \"CleaningFee\", \"ExtraGuestFee\", " +
            "\"ExtraBedFee\", \"AllowsSmoking\", \"AllowsParties\", \"AllowsPets\", \"CancellationFreeCancellationDays\", " +
            "\"CancellationDepositRefundable\", \"IsPublished\", \"CreatedAt\", \"UpdatedAt\", \"IsDeleted\", \"CurrencyCode\") " +
            "SELECT gen_random_uuid(), gen_random_uuid(), t.\"Id\", 'x', 'x', 1, 1, 1, '14:00', '11:00', false, true, true, " +
            "'Exact', 0, 0, 0, false, false, false, 0, true, false, now(), now(), false, 'USD' " +
            "FROM t"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal("FK_ShortStayListings_UserAccounts_OwnerId", error.ConstraintName);
    }

    [Fact(DisplayName = "A rating outside 1..5 is refused by the database")]
    public async Task RatingOutOfRange_IsRefused()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The check is evaluated before the foreign keys of the same row, so no parent rows are needed.
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"PropertyReviews\" (\"Id\", \"PropertyId\", \"ReviewerId\", \"Rating\", \"CreatedAt\", \"UpdatedAt\", \"IsDeleted\") " +
            "VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 6, now(), now(), false)"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_PropertyReviews_Rating", error.ConstraintName);
    }
}
