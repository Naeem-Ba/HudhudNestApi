using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;
using PropertyApi.Domain.ShortStay.ValueObjects;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.ShortStay;
using PropertyApi.Integration.Tests.Auth;
using Xunit;

namespace PropertyApi.Integration.Tests.Bookings;

/// <summary>
/// HasActiveBookingsForListingAsync guards listing deletion. It reaches the listing through
/// Unit -> RoomType, so only a real server proves the join and the status set.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "ShortStay")]
public sealed class ShortStayActiveBookingsQueryTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "A pending booking counts as active; a rejected or cancelled one does not; other listings do not")]
    public async Task OnlyInProgressBookingsOfThatListingCount()
    {
        var host = await _factory.SeedUserAsync($"ssa-host-{Guid.NewGuid():N}@test.local");
        var guest = await _factory.SeedUserAsync($"ssa-guest-{Guid.NewGuid():N}@test.local");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = new BookingRepository(db);

        var (activeListing, activeUnit) = await SeedListingAsync(db, host.UserAccountId, "active");
        var (closedListing, closedUnit) = await SeedListingAsync(db, host.UserAccountId, "closed");
        var (emptyListing, _) = await SeedListingAsync(db, host.UserAccountId, "empty");

        db.ShortStayBookings.Add(NewBooking(activeUnit.Id, guest.UserAccountId, 10));
        var rejected = NewBooking(closedUnit.Id, guest.UserAccountId, 20);
        rejected.Reject("dates taken");
        var cancelled = NewBooking(closedUnit.Id, guest.UserAccountId, 30);
        cancelled.Cancel(guest.UserAccountId, "plans changed");
        db.ShortStayBookings.AddRange(rejected, cancelled);
        await db.SaveChangesAsync();

        Assert.True(await repository.HasActiveBookingsForListingAsync(activeListing.Id));
        Assert.False(await repository.HasActiveBookingsForListingAsync(closedListing.Id));
        Assert.False(await repository.HasActiveBookingsForListingAsync(emptyListing.Id));
    }

    private static async Task<(ShortStayListing Listing, AccommodationUnit Unit)> SeedListingAsync(
        AppDbContext db, Guid ownerId, string label)
    {
        var accommodationType = await db.AccommodationTypes.FirstOrDefaultAsync();
        if (accommodationType is null)
        {
            accommodationType = AccommodationType.Create($"test-{Guid.NewGuid():N}"[..20], "شقة", "Apartment", "Residential", null, 1);
            db.AccommodationTypes.Add(accommodationType);
            await db.SaveChangesAsync();
        }

        var listing = ShortStayListing.Create(
            ownerId, accommodationType.Id, $"إعلان {label}", "وصف كافٍ للإعلان",
            capacity: 4, bedrooms: 2, bathrooms: 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m);
        var roomType = new RoomType { ShortStayListing = listing, Name = "Standard", BasePricePerNight = 50m };
        var unit = new AccommodationUnit { RoomType = roomType, Label = label };
        db.ShortStayListings.Add(listing);
        db.Add(roomType);
        db.Add(unit);
        await db.SaveChangesAsync();
        return (listing, unit);
    }

    private static Booking NewBooking(Guid unitId, Guid guestId, int daysAhead) => Booking.Create(
        unitId, guestId,
        DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(daysAhead)),
        DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(daysAhead + 2)),
        GuestComposition.Create(2, 0, 0), BookingMode.Request, PaymentMethod.PayOnArrival,
        totalAmount: 100m, depositAmount: 0m,
        cancellationPolicyFreeCancellationDays: 3, cancellationPolicyDepositRefundable: true,
        cancellationPolicyCustomTermsText: null, houseRulesSnapshotText: "No parties", houseRulesAccepted: true);
}
