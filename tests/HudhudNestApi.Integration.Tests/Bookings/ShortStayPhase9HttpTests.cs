using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Messaging;

namespace HudhudNestApi.Integration.Tests.Bookings;

/// <summary>
/// QA Phase 9 fixes over real HTTP and PostgreSQL (short-stay): rules that were replaced or cleared keep
/// pricing bookings (P9-D01), a listing deleted at the moment a booking is created leaves an orphan booking
/// (P9-D02), a long review comment ended in HTTP 500 (P9-D05), the anonymous search failed on NUL and treated
/// "%" as a wildcard (P9-D07), publishing twice moved a listing to the top (P9-D09), and impossible dates
/// were ignored by the search (P9-D10).
/// </summary>
[Trait("Feature", "ShortStay")]
public sealed class ShortStayPhase9HttpTests : IClassFixture<MessagingApiTestFactory>, IAsyncLifetime
{
    private readonly MessagingApiTestFactory _factory;

    public ShortStayPhase9HttpTests(MessagingApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Seeded(Guid ListingId, Guid RoomTypeId, Guid UnitId);

    private static string D(int daysFromToday) => DateTime.UtcNow.Date.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private async Task<Seeded> SeedPublishedListingAsync(Guid ownerId, string? city = null, bool instant = false)
    {
        return await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var type = await db.AccommodationTypes.FirstOrDefaultAsync();
            if (type is null)
            {
                type = AccommodationType.Create($"test-{Guid.NewGuid():N}"[..20], "شقة", "Apartment", "Residential", null, 1);
                db.AccommodationTypes.Add(type);
                await db.SaveChangesAsync();
            }

            var listing = ShortStayListing.Create(
                ownerId, type.Id, "شقة مفروشة قرب المدينة القديمة", "وصف كافٍ للإعلان يشرح المكان والخدمات",
                capacity: 4, bedrooms: 2, bathrooms: 1,
                new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m);
            var roomType = new RoomType { ShortStayListing = listing, Name = "Standard", BasePricePerNight = 50m };
            var unit = new AccommodationUnit { RoomType = roomType, Label = "A1" };
            db.ShortStayListings.Add(listing);
            db.Add(roomType);
            db.Add(unit);
            await db.SaveChangesAsync();
            db.Entry(listing).Property(nameof(ShortStayListing.City)).CurrentValue = city ?? "دمشق";
            listing.Publish();
            await db.SaveChangesAsync();
            return new Seeded(listing.Id, roomType.Id, unit.Id);
        });
    }

    private static object BookingBody(Guid unitId, int from, int to) => new
    {
        unitId, checkIn = D(from), checkOut = D(to), adults = 2, children = 0, infants = 0,
        paymentMethod = "PayOnArrival", houseRulesAccepted = true
    };

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<decimal> PreviewTotalAsync(HttpClient client, Guid unitId, int from, int to)
    {
        var response = await client.GetAsync(
            $"/api/short-stay/bookings/pricing-preview?unitId={unitId}&checkIn={D(from)}&checkOut={D(to)}&adults=2&children=0&infants=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJson(response)).GetProperty("totalAmount").GetDecimal();
    }

    // ── P9-D01 ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "P9-D01: replacing or clearing price rules and minimum-stay rules takes effect at once")]
    public async Task ReplacedAndClearedRules_StopApplying()
    {
        var host = await _factory.SeedUserAsync("host");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var anonymous = _factory.CreateClient();

        Assert.Equal(100m, await PreviewTotalAsync(anonymous, seeded.UnitId, 30, 32));

        object[] Seasonal(decimal price) => new object[]
        {
            new { ruleType = "Seasonal", dayOfWeek = (int?)null, dateRangeStart = D(25), dateRangeEnd = D(40), pricePerNight = price }
        };

        Assert.Equal(HttpStatusCode.NoContent,
            (await hostClient.PutAsJsonAsync($"/api/short-stay/listings/room-types/{seeded.RoomTypeId}/pricing-rules", Seasonal(120))).StatusCode);
        Assert.Equal(240m, await PreviewTotalAsync(anonymous, seeded.UnitId, 30, 32));

        // Replaced: the old 120 must not survive next to the new 90.
        Assert.Equal(HttpStatusCode.NoContent,
            (await hostClient.PutAsJsonAsync($"/api/short-stay/listings/room-types/{seeded.RoomTypeId}/pricing-rules", Seasonal(90))).StatusCode);
        Assert.Equal(180m, await PreviewTotalAsync(anonymous, seeded.UnitId, 30, 32));

        // Cleared: back to the base price.
        Assert.Equal(HttpStatusCode.NoContent,
            (await hostClient.PutAsJsonAsync($"/api/short-stay/listings/room-types/{seeded.RoomTypeId}/pricing-rules", Array.Empty<object>())).StatusCode);
        Assert.Equal(100m, await PreviewTotalAsync(anonymous, seeded.UnitId, 30, 32));

        // Minimum stay: a 5-night rule is replaced by a 1-night rule, then cleared.
        await hostClient.PutAsJsonAsync($"/api/short-stay/listings/room-types/{seeded.RoomTypeId}/minimum-stay-rules",
            new[] { new { minimumNights = 5, dateRangeStart = (string?)null, dateRangeEnd = (string?)null } });
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.GetAsync($"/api/short-stay/bookings/pricing-preview?unitId={seeded.UnitId}&checkIn={D(30)}&checkOut={D(32)}&adults=2&children=0&infants=0")).StatusCode);

        await hostClient.PutAsJsonAsync($"/api/short-stay/listings/room-types/{seeded.RoomTypeId}/minimum-stay-rules",
            new[] { new { minimumNights = 1, dateRangeStart = (string?)null, dateRangeEnd = (string?)null } });
        Assert.Equal(100m, await PreviewTotalAsync(anonymous, seeded.UnitId, 30, 32));

        await hostClient.PutAsJsonAsync($"/api/short-stay/listings/room-types/{seeded.RoomTypeId}/minimum-stay-rules",
            Array.Empty<object>());
        Assert.Equal(100m, await PreviewTotalAsync(anonymous, seeded.UnitId, 30, 32));

        // The removed rows are still in the table (soft delete), just invisible.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.True(await db.ShortStayPricingRules.IgnoreQueryFilters().CountAsync(r => r.IsDeleted) >= 2);
            Assert.Equal(0, await db.ShortStayPricingRules.CountAsync());
        });
    }

    // ── P9-D02 ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "P9-D02: deleting a listing and booking it at the same moment never leaves a live booking on a deleted listing")]
    public async Task DeleteRacingWithBooking_NeverLeavesAnOrphan()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        for (var round = 0; round < 6; round++)
        {
            var seeded = await SeedPublishedListingAsync(host.Id);

            var create = guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
            var delete = hostClient.DeleteAsync($"/api/short-stay/listings/{seeded.ListingId}");
            await Task.WhenAll(create, delete);

            await _factory.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var deleted = await db.ShortStayListings.IgnoreQueryFilters().AnyAsync(l => l.Id == seeded.ListingId && l.IsDeleted);
                var live = await db.ShortStayBookings.CountAsync(b => b.UnitId == seeded.UnitId
                    && (b.Status == HudhudNestApi.Domain.ShortStay.Enums.BookingStatus.Pending
                        || b.Status == HudhudNestApi.Domain.ShortStay.Enums.BookingStatus.Approved));
                Assert.False(deleted && live > 0,
                    $"round {round}: booking {create.Result.StatusCode}, delete {delete.Result.StatusCode}, deleted={deleted}, live bookings={live}");
            });
        }
    }

    [Fact(DisplayName = "P9-D02: a booking left on a deleted listing can still be cancelled by the guest and rejected by the host")]
    public async Task BookingOnADeletedListing_CanBeClosed()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        async Task<Guid> BookAsync(int from, int to)
        {
            var created = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, from, to));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await ReadJson(created)).GetProperty("id").GetGuid();
        }

        var first = await BookAsync(30, 32);
        var second = await BookAsync(40, 42);

        // The state a lost race (or an older deployment) leaves behind: the listing is gone, the bookings are not.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"ShortStayListings\" SET \"IsDeleted\" = TRUE, \"DeletedAt\" = NOW() WHERE \"Id\" = {0}", seeded.ListingId);
        });

        Assert.Equal(HttpStatusCode.OK, (await guestClient.DeleteAsync($"/api/short-stay/bookings/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/short-stay/bookings/{second}/reject", null)).StatusCode);

        // Nothing else becomes possible on a deleted listing.
        Assert.Equal(HttpStatusCode.NotFound, (await hostClient.PutAsync($"/api/short-stay/bookings/{second}/approve", null)).StatusCode);
    }

    // ── P9-D05 ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "P9-D05: a review comment over 1000 characters is a 422, not a 500, and 1000 is accepted")]
    public async Task ReviewComment_IsLengthChecked()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        var created = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
        var bookingId = (await ReadJson(created)).GetProperty("id").GetGuid();
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE \"ShortStayBookings\" SET \"Status\" = 'Completed' WHERE \"Id\" = {0}", bookingId);
        });

        var tooLong = await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);

        var withNul = await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = "bad\0text" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withNul.StatusCode);

        var badRating = await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 6, comment = "ok" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badRating.StatusCode);

        var ok = await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = new string('x', 1000) });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    // ── P9-D07 / P9-D10 ──────────────────────────────────────────────────

    [Fact(DisplayName = "P9-D07: the anonymous search rejects NUL in the city and treats % and _ as plain text")]
    public async Task Search_CityText_IsSafe()
    {
        var host = await _factory.SeedUserAsync("host");
        await SeedPublishedListingAsync(host.Id, city: "دمشق");
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.GetAsync("/api/short-stay/listings?city=%00")).StatusCode);

        foreach (var wildcard in new[] { "%25", "_", "%25%25" })
        {
            var response = await anonymous.GetAsync($"/api/short-stay/listings?city={wildcard}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await ReadJson(response)).GetProperty("totalCount").GetInt32());
        }

        var real = await anonymous.GetAsync($"/api/short-stay/listings?city={Uri.EscapeDataString("دمشق")}");
        Assert.Equal(1, (await ReadJson(real)).GetProperty("totalCount").GetInt32());
    }

    [Theory(DisplayName = "P9-D10: the search refuses impossible or one-sided dates instead of ignoring them")]
    [InlineData(40, 30, "reversed")]
    [InlineData(30, 30, "empty range")]
    [InlineData(-30, -28, "in the past")]
    [InlineData(30, 600, "longer than a year")]
    public async Task Search_RejectsImpossibleDates(int from, int to, string why)
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/short-stay/listings?checkIn={D(from)}&checkOut={D(to)}");
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{why}: {response.StatusCode}");
    }

    [Fact(DisplayName = "P9-D10: a search with only one date is refused and a valid range still works")]
    public async Task Search_OneSidedDate_IsRefused_ValidRangeWorks()
    {
        var host = await _factory.SeedUserAsync("host");
        await SeedPublishedListingAsync(host.Id);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.GetAsync($"/api/short-stay/listings?checkIn={D(30)}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await anonymous.GetAsync($"/api/short-stay/listings?checkOut={D(30)}")).StatusCode);

        var ok = await anonymous.GetAsync($"/api/short-stay/listings?checkIn={D(30)}&checkOut={D(33)}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(1, (await ReadJson(ok)).GetProperty("totalCount").GetInt32());
    }

    // ── P9-D09 ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "P9-D09: publishing a live listing again does not move it to the top of the search")]
    public async Task PublishTwice_KeepsPublishedAt()
    {
        var host = await _factory.SeedUserAsync("host");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);

        async Task<DateTime?> PublishedAtAsync() => await _factory.InScopeAsync(async services =>
            (await services.GetRequiredService<AppDbContext>().ShortStayListings.AsNoTracking().SingleAsync(l => l.Id == seeded.ListingId)).PublishedAt);

        var before = await PublishedAtAsync();
        await Task.Delay(50);
        Assert.Equal(HttpStatusCode.NoContent, (await hostClient.PutAsync($"/api/short-stay/listings/{seeded.ListingId}/publish", null)).StatusCode);
        Assert.Equal(before, await PublishedAtAsync());

        // Unpublish then publish is a real republication and does refresh it.
        await hostClient.PutAsync($"/api/short-stay/listings/{seeded.ListingId}/unpublish", null);
        await Task.Delay(50);
        await hostClient.PutAsync($"/api/short-stay/listings/{seeded.ListingId}/publish", null);
        Assert.True(await PublishedAtAsync() > before);
    }
}
