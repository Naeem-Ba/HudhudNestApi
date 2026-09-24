using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.ShortStay.Queries.GetHostBookingRequests;
using PropertyApi.Application.ShortStay.Queries.GetMyShortStayBookings;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;
using PropertyApi.Domain.ShortStay.ValueObjects;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.ShortStay;
using PropertyApi.Integration.Tests.Auth;
using Xunit;

namespace PropertyApi.Integration.Tests.Bookings;

/// <summary>
/// A booking outlives its listing: once a host soft-deletes a listing, the listing's global query filter
/// hides it, but the booking rows (money, dates, history) are still the guest's and the host's.
/// The bookings queries reach the listing through the required Unit -> RoomType -> ShortStayListing
/// chain; with the filter applied that chain is an inner join, so the booking vanished from both the
/// guest's and the host's list. Only a real server shows this -- the filter and the joins are SQL.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "ShortStay")]
public sealed class ShortStayBookingsOfDeletedListingTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Guest and host still see a booking after the host deletes its listing")]
    public async Task BookingsOfADeletedListing_StayVisibleToGuestAndHost()
    {
        var host = await _factory.SeedUserAsync($"ss-host-{Guid.NewGuid():N}@test.local");
        var guest = await _factory.SeedUserAsync($"ss-guest-{Guid.NewGuid():N}@test.local");

        Guid listingId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accommodationType = await db.AccommodationTypes.FirstOrDefaultAsync();
            if (accommodationType is null)
            {
                accommodationType = AccommodationType.Create($"test-{Guid.NewGuid():N}"[..20], "شقة", "Apartment", "Residential", null, 1);
                db.AccommodationTypes.Add(accommodationType);
                await db.SaveChangesAsync();
            }

            var listing = ShortStayListing.Create(
                host.UserAccountId, accommodationType.Id, "شقة على البحر", "وصف كافٍ للإعلان",
                capacity: 4, bedrooms: 2, bathrooms: 1,
                new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m);
            var roomType = new RoomType { ShortStayListing = listing, Name = "Standard", BasePricePerNight = 50m };
            var unit = new AccommodationUnit { RoomType = roomType, Label = "A1" };
            db.ShortStayListings.Add(listing);
            db.Add(roomType);
            db.Add(unit);
            db.ShortStayListingPhotos.Add(new ShortStayListingPhoto
            {
                ShortStayListing = listing,
                Url = "https://example.test/p.jpg",
                PublicId = "test/p"
            });
            await db.SaveChangesAsync();
            listingId = listing.Id;

            var booking = Booking.Create(
                unit.Id, guest.UserAccountId,
                DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(10)),
                DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(12)),
                GuestComposition.Create(2, 0, 0), BookingMode.Request, PaymentMethod.PayOnArrival,
                totalAmount: 100m, depositAmount: 0m,
                cancellationPolicyFreeCancellationDays: 3, cancellationPolicyDepositRefundable: true,
                cancellationPolicyCustomTermsText: null, houseRulesSnapshotText: "No parties", houseRulesAccepted: true);
            db.ShortStayBookings.Add(booking);
            await db.SaveChangesAsync();

            listing.MarkAsDeleted(host.UserAccountId);
            await db.SaveChangesAsync();
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = new BookingRepository(readScope.ServiceProvider.GetRequiredService<AppDbContext>());

        var guestBookings = await new GetMyShortStayBookingsQueryHandler(repository)
            .Handle(new GetMyShortStayBookingsQuery(guest.UserAccountId), CancellationToken.None);
        var hostBookings = await new GetHostBookingRequestsQueryHandler(repository)
            .Handle(new GetHostBookingRequestsQuery(host.UserAccountId), CancellationToken.None);

        var mine = Assert.Single(guestBookings);
        Assert.Equal(listingId, mine.ShortStayListingId);
        Assert.Equal("شقة على البحر", mine.ListingTitle);
        Assert.Single(hostBookings);

        // Photos, by contrast, have no life of their own: they follow the listing's filter.
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await readDb.ShortStayListingPhotos.AnyAsync(p => p.ShortStayListingId == listingId));
    }
}
