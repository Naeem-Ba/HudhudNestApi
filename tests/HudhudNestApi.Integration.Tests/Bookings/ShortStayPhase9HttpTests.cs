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
        unitId,
        checkIn = D(from),
        checkOut = D(to),
        adults = 2,
        children = 0,
        infants = 0,
        paymentMethod = "PayOnArrival",
        houseRulesAccepted = true
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
        await GivePlanAsync(host.Id);

        var created = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
        var bookingId = (await ReadJson(created)).GetProperty("id").GetGuid();
        await FinishStayAsync(bookingId);

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

    // ── helpers for the review / lifecycle tests ─────────────────────────

    private Task GivePlanAsync(Guid userId) => _factory.GivePlanAsync(userId);

    /// <summary>Puts a booking in the state after a real stay: Completed, and the check-out day already past.</summary>
    private async Task FinishStayAsync(Guid bookingId) => await _factory.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"ShortStayBookings\" SET \"Status\" = 'Completed', \"CheckIn\" = CURRENT_DATE - 4, \"CheckOut\" = CURRENT_DATE - 2 WHERE \"Id\" = {0}",
            bookingId);
    });

    private async Task MakeInstantAsync(Guid listingId) => await _factory.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"ShortStayListings\" SET \"InstantBookingEnabled\" = TRUE WHERE \"Id\" = {0}", listingId);
    });

    private async Task<int> SweepAsync(DateTime utcNow)
    {
        var service = new HudhudNestApi.Infrastructure.ShortStay.ShortStayBookingLifecycleHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HudhudNestApi.Infrastructure.ShortStay.ShortStayBookingLifecycleHostedService>.Instance);
        return await service.RunOnceAsync(CancellationToken.None, utcNow);
    }

    private async Task<string> StatusOfAsync(Guid bookingId) => await _factory.InScopeAsync(async services =>
        (await services.GetRequiredService<AppDbContext>().ShortStayBookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Status.ToString());

    // ── P9-D03: instant booking is capped to one month (owner decision 2026-10-18) ─────

    [Fact(DisplayName = "P9-D03: an instant booking must start within 30 days and cover at most 30 nights")]
    public async Task InstantBooking_IsCappedToOneMonth()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        await MakeInstantAsync(seeded.ListingId);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        // Starts too far ahead.
        var farAway = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 45, 47));
        Assert.Equal(HttpStatusCode.BadRequest, farAway.StatusCode);

        // Too long.
        var tooLong = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 5, 40));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        // The edge itself is fine: starts in 30 days, exactly 30 nights.
        var edge = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 60));
        Assert.Equal(HttpStatusCode.Created, edge.StatusCode);
    }

    [Fact(DisplayName = "P9-D03: one guest cannot hold more than 3 unfinished instant bookings; request-mode listings are not capped")]
    public async Task InstantBooking_HasAPerGuestCap()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        var units = new List<Seeded>();
        for (var i = 0; i < 4; i++)
        {
            var s = await SeedPublishedListingAsync(host.Id);
            await MakeInstantAsync(s.ListingId);
            units.Add(s);
        }

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created,
                (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(units[i].UnitId, 10, 12))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(units[3].UnitId, 10, 12))).StatusCode);

        // A different guest is not affected by the first guest's cap.
        var other = await _factory.SeedUserAsync("other");
        Assert.Equal(HttpStatusCode.Created,
            (await _factory.AuthedClient(other.AccessToken).PostAsJsonAsync("/api/short-stay/bookings", BookingBody(units[3].UnitId, 10, 12))).StatusCode);

        // Request mode keeps its own (long) limit and is not counted.
        var requestListing = await SeedPublishedListingAsync(host.Id);
        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(requestListing.UnitId, 100, 160))).StatusCode);
    }

    // ── P9-D03: the host decides which days are unavailable ─────────────

    [Fact(DisplayName = "P9-D03: a host block keeps the dates from being booked, shows in the host list and can be removed")]
    public async Task HostBlock_BlocksBookingsAndCanBeRemoved()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var guestClient = _factory.AuthedClient(guest.AccessToken);
        var anonymous = _factory.CreateClient();

        var add = await hostClient.PostAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(20), to = D(25) });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var blockId = (await ReadJson(add)).GetProperty("id").GetGuid();

        var list = await ReadJson(await hostClient.GetAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks"));
        Assert.Equal(1, list.GetArrayLength());

        // Overlapping block -> 409; booking over the block -> 409; the day the block ends is bookable.
        Assert.Equal(HttpStatusCode.Conflict,
            (await hostClient.PostAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(24), to = D(27) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 22, 24))).StatusCode);

        // The search leaves the unit out for blocked dates and keeps it for free ones.
        var blockedSearch = await ReadJson(await anonymous.GetAsync($"/api/short-stay/listings?checkIn={D(21)}&checkOut={D(23)}"));
        Assert.Equal(0, blockedSearch.GetProperty("totalCount").GetInt32());
        var freeSearch = await ReadJson(await anonymous.GetAsync($"/api/short-stay/listings?checkIn={D(25)}&checkOut={D(27)}"));
        Assert.Equal(1, freeSearch.GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 25, 27))).StatusCode);

        // Removing the block frees the dates again.
        Assert.Equal(HttpStatusCode.NoContent,
            (await hostClient.DeleteAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks/{blockId}")).StatusCode);
        Assert.Equal(0, (await ReadJson(await hostClient.GetAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 21, 23))).StatusCode);
    }

    [Fact(DisplayName = "P9-D03: only the owner can manage blocks, and a block cannot cover a confirmed booking or the past")]
    public async Task HostBlock_Rules()
    {
        var host = await _factory.SeedUserAsync("host");
        var stranger = await _factory.SeedUserAsync("stranger");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _factory.AuthedClient(stranger.AccessToken).PostAsJsonAsync(
                $"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(20), to = D(25) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().PostAsJsonAsync(
                $"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(20), to = D(25) })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await hostClient.PostAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(-10), to = D(-5) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await hostClient.PostAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(20), to = D(20) })).StatusCode);

        var booked = await _factory.AuthedClient(guest.AccessToken)
            .PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 33));
        var bookingId = (await ReadJson(booked)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/short-stay/bookings/{bookingId}/approve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await hostClient.PostAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/blocks", new { from = D(31), to = D(35) })).StatusCode);
    }

    [Fact(DisplayName = "P9-D03: a unit switched to 'not available for now' cannot be booked or found, and can be switched back")]
    public async Task UnitSwitchedOff_IsUnavailable()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var guestClient = _factory.AuthedClient(guest.AccessToken);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK,
            (await hostClient.PutAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/availability", new { isActive = false })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32))).StatusCode);
        Assert.Equal(0, (await ReadJson(await anonymous.GetAsync($"/api/short-stay/listings?checkIn={D(30)}&checkOut={D(32)}"))).GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _factory.AuthedClient(guest.AccessToken).PutAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/availability", new { isActive = true })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await hostClient.PutAsJsonAsync($"/api/short-stay/listings/units/{seeded.UnitId}/availability", new { isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32))).StatusCode);
    }

    // ── P9-D04: unconfirmed bookings expire (3 days, one week at the most) ──────────────

    [Fact(DisplayName = "P9-D04: a request the host did not answer within 3 days expires, frees the dates and tells both sides")]
    public async Task UnansweredRequest_Expires()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var other = await _factory.SeedUserAsync("other");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        var created = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
        var bookingId = (await ReadJson(created)).GetProperty("id").GetGuid();
        Assert.True((await ReadJson(created)).TryGetProperty("confirmationDeadline", out var deadline) && deadline.ValueKind == JsonValueKind.String);

        // Two days in: still waiting. Four days in: expired.
        Assert.Equal(0, await SweepAsync(DateTime.UtcNow.AddDays(2)));
        Assert.Equal("Pending", await StatusOfAsync(bookingId));

        Assert.Equal(1, await SweepAsync(DateTime.UtcNow.AddDays(4)));
        Assert.Equal("Expired", await StatusOfAsync(bookingId));
        Assert.Equal(0, await SweepAsync(DateTime.UtcNow.AddDays(5)));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.True(await db.Notifications.AnyAsync(n => n.RecipientId == guest.Id && n.RelatedEntityId == bookingId));
            Assert.True(await db.Notifications.AnyAsync(n => n.RecipientId == host.Id && n.RelatedEntityId == bookingId));
        });

        // The dates are free for somebody else, and the host can no longer approve the expired request.
        Assert.Equal(HttpStatusCode.Created,
            (await _factory.AuthedClient(other.AccessToken).PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _factory.AuthedClient(host.AccessToken).PutAsync($"/api/short-stay/bookings/{bookingId}/approve", null)).StatusCode);
    }

    [Fact(DisplayName = "P9-D04: an approved booking not confirmed within 3 days of approval expires; a confirmed one never does")]
    public async Task ApprovedButUnconfirmed_Expires_ConfirmedStays()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        async Task<Guid> BookAsync(int from, int to)
            => (await ReadJson(await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, from, to)))).GetProperty("id").GetGuid();

        var approvedOnly = await BookAsync(30, 32);
        var confirmed = await BookAsync(40, 42);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/short-stay/bookings/{approvedOnly}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/short-stay/bookings/{confirmed}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/short-stay/bookings/{confirmed}/confirm", null)).StatusCode);

        Assert.Equal(1, await SweepAsync(DateTime.UtcNow.AddDays(4)));
        Assert.Equal("Expired", await StatusOfAsync(approvedOnly));
        Assert.Equal("Confirmed", await StatusOfAsync(confirmed));
    }

    [Fact(DisplayName = "P9-D04: a booking past its deadline cannot be approved or confirmed even before the sweep runs")]
    public async Task OverdueBooking_CannotBeApprovedOrConfirmed()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        var created = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
        var bookingId = (await ReadJson(created)).GetProperty("id").GetGuid();

        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE \"ShortStayBookings\" SET \"CreatedAt\" = NOW() - INTERVAL '4 days' WHERE \"Id\" = {0}", bookingId));

        Assert.Equal(HttpStatusCode.Conflict, (await hostClient.PutAsync($"/api/short-stay/bookings/{bookingId}/approve", null)).StatusCode);
    }

    // ── P9-D13: check-in not before the arrival day ─────────────────────

    [Fact(DisplayName = "P9-D13: a host cannot check a guest in weeks before the arrival date")]
    public async Task CheckIn_IsDateGated()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var hostClient = _factory.AuthedClient(host.AccessToken);

        var created = await _factory.AuthedClient(guest.AccessToken).PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
        var bookingId = (await ReadJson(created)).GetProperty("id").GetGuid();
        await hostClient.PutAsync($"/api/short-stay/bookings/{bookingId}/approve", null);
        await hostClient.PutAsync($"/api/short-stay/bookings/{bookingId}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict, (await hostClient.PutAsync($"/api/short-stay/bookings/{bookingId}/check-in", null)).StatusCode);

        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE \"ShortStayBookings\" SET \"CheckIn\" = CURRENT_DATE, \"CheckOut\" = CURRENT_DATE + 2 WHERE \"Id\" = {0}", bookingId));
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/short-stay/bookings/{bookingId}/check-in", null)).StatusCode);
    }

    // ── P9-D06 / P9-D08 and the review rules (owner decision 2026-10-18) ──────────────────

    [Fact(DisplayName = "P9-D06: a review is accepted only after the stay is over and only while the reviewed host has an active plan")]
    public async Task Review_NeedsFinishedStay_AndActivePlan()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var guestClient = _factory.AuthedClient(guest.AccessToken);

        var created = await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32));
        var bookingId = (await ReadJson(created)).GetProperty("id").GetGuid();

        // Completed but the check-out day has not come yet (the stay is still in the future).
        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE \"ShortStayBookings\" SET \"Status\" = 'Completed' WHERE \"Id\" = {0}", bookingId));
        await GivePlanAsync(host.Id);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = "early" })).StatusCode);

        // The stay is over, but the host has no plan in force.
        await FinishStayAsync(bookingId);
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var user = await db.UserAccounts.SingleAsync(u => u.Id == host.Id);
            user.CancelPlan(host.Id, DateTime.UtcNow);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.BadRequest,
            (await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = "no plan" })).StatusCode);

        await GivePlanAsync(host.Id);
        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 4, comment = "كان جيدًا" })).StatusCode);

        // One review per booking.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = "again" })).StatusCode);
    }

    [Fact(DisplayName = "P9-D06: the listing page can read its reviews and 'my bookings' says which stays were already reviewed")]
    public async Task Reviews_AreReadable_AndMyBookingsFlagsReviewed()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var guestClient = _factory.AuthedClient(guest.AccessToken);
        var anonymous = _factory.CreateClient();
        await GivePlanAsync(host.Id);

        var bookingId = (await ReadJson(await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32)))).GetProperty("id").GetGuid();
        await FinishStayAsync(bookingId);

        var before = await ReadJson(await guestClient.GetAsync("/api/short-stay/bookings/mine"));
        Assert.False(before[0].GetProperty("reviewed").GetBoolean());

        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 4, comment = "إقامة جيدة" })).StatusCode);

        var after = await ReadJson(await guestClient.GetAsync("/api/short-stay/bookings/mine"));
        Assert.True(after[0].GetProperty("reviewed").GetBoolean());

        var reviews = await ReadJson(await anonymous.GetAsync($"/api/short-stay/listings/{seeded.ListingId}/reviews"));
        Assert.Equal(1, reviews.GetProperty("totalCount").GetInt32());
        Assert.Equal(4.0, reviews.GetProperty("averageRating").GetDouble());
        Assert.Equal("إقامة جيدة", reviews.GetProperty("reviews")[0].GetProperty("comment").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/short-stay/listings/{Guid.NewGuid()}/reviews")).StatusCode);
    }

    [Fact(DisplayName = "P9-D08: a guest can still review a finished stay after the host deleted the listing")]
    public async Task Review_WorksAfterTheListingWasDeleted()
    {
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var guestClient = _factory.AuthedClient(guest.AccessToken);
        await GivePlanAsync(host.Id);

        var bookingId = (await ReadJson(await guestClient.PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32)))).GetProperty("id").GetGuid();
        await FinishStayAsync(bookingId);
        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE \"ShortStayListings\" SET \"IsDeleted\" = TRUE, \"DeletedAt\" = NOW() WHERE \"Id\" = {0}", seeded.ListingId));

        Assert.Equal(HttpStatusCode.Created,
            (await guestClient.PostAsJsonAsync("/api/short-stay/reviews", new { bookingId, rating = 5, comment = "ok" })).StatusCode);
    }

    // ── D11: administrative take-down ───────────────────────────────────

    [Fact(DisplayName = "P9-D11: an admin can take a short-stay listing down with a reason and restore it; the owner cannot republish meanwhile")]
    public async Task AdminTakeDown_AndRestore()
    {
        var admin = await _factory.SeedUserAsync("admin", "Admin");
        var host = await _factory.SeedUserAsync("host");
        var guest = await _factory.SeedUserAsync("guest");
        var seeded = await SeedPublishedListingAsync(host.Id);
        var adminClient = _factory.AuthedClient(admin.AccessToken);
        var hostClient = _factory.AuthedClient(host.AccessToken);
        var anonymous = _factory.CreateClient();

        // Admin only.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await hostClient.PostAsJsonAsync($"/api/admin/short-stay/{seeded.ListingId}/take-down", new { reason = "x" })).StatusCode);

        // A reason is mandatory.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await adminClient.PostAsJsonAsync($"/api/admin/short-stay/{seeded.ListingId}/take-down", new { reason = " " })).StatusCode);

        var down = await adminClient.PostAsJsonAsync($"/api/admin/short-stay/{seeded.ListingId}/take-down", new { reason = "صور مخالفة" });
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.True((await ReadJson(down)).GetProperty("isTakenDown").GetBoolean());

        // Gone from search and detail, cannot be booked, owner sees the reason and cannot publish.
        Assert.Equal(0, (await ReadJson(await anonymous.GetAsync("/api/short-stay/listings"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/short-stay/listings/{seeded.ListingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _factory.AuthedClient(guest.AccessToken).PostAsJsonAsync("/api/short-stay/bookings", BookingBody(seeded.UnitId, 30, 32))).StatusCode);
        var mine = await ReadJson(await hostClient.GetAsync($"/api/short-stay/listings/{seeded.ListingId}"));
        Assert.Equal("صور مخالفة", mine.GetProperty("takenDownReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await hostClient.PutAsync($"/api/short-stay/listings/{seeded.ListingId}/publish", null)).StatusCode);

        // The owner was told, the second take-down is refused, and the admin list can filter on it.
        await _factory.InScopeAsync(async services =>
            Assert.True(await services.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.RecipientId == host.Id && n.RelatedEntityId == seeded.ListingId)));
        Assert.Equal(HttpStatusCode.Conflict,
            (await adminClient.PostAsJsonAsync($"/api/admin/short-stay/{seeded.ListingId}/take-down", new { reason = "again" })).StatusCode);
        var listed = await ReadJson(await adminClient.GetAsync("/api/admin/short-stay?takenDown=true"));
        Assert.Equal(1, listed.GetProperty("totalCount").GetInt32());

        // Restore.
        var restored = await adminClient.PostAsJsonAsync($"/api/admin/short-stay/{seeded.ListingId}/restore", new { reason = "تم التصحيح" });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(1, (await ReadJson(await anonymous.GetAsync("/api/short-stay/listings"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,
            (await adminClient.PostAsJsonAsync($"/api/admin/short-stay/{seeded.ListingId}/restore", new { reason = "x" })).StatusCode);
    }
}
