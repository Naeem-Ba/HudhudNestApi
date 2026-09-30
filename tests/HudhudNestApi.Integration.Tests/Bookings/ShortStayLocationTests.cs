using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.ShortStay;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Bookings;

/// <summary>
/// The location rules that only a real server proves: nullable coordinates persist as NULL (not 0),
/// the resolver validates the governorate → district → neighborhood chain against real rows, and the
/// city search also matches the governorate's English name.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "ShortStay")]
public sealed class ShortStayLocationTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Resolver returns the governorate's Arabic name and rejects a district of another governorate")]
    public async Task Resolver_DerivesCity_AndValidatesTheChain()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var damascus = Governorate.Create($"دمشق-{suffix}", $"Damascus-{suffix}", "SY");
        var aleppo = Governorate.Create($"حلب-{suffix}", $"Aleppo-{suffix}", "SY");
        db.Governorates.AddRange(damascus, aleppo);
        await db.SaveChangesAsync();
        var mezzeh = District.Create(damascus.Id, "المزة", "Mezzeh");
        db.Districts.Add(mezzeh);
        await db.SaveChangesAsync();

        var resolver = new ShortStayLocationResolver(db);

        Assert.Equal(damascus.NameAr, await resolver.ResolveCityAsync(damascus.Id, mezzeh.Id, null, "ignored", default));
        Assert.Equal("Free text", await resolver.ResolveCityAsync(null, null, null, "  Free text ", default));
        Assert.Null(await resolver.ResolveCityAsync(null, null, null, "  ", default));

        await Assert.ThrowsAsync<ValidationException>(
            () => resolver.ResolveCityAsync(aleppo.Id, mezzeh.Id, null, null, default));
        await Assert.ThrowsAsync<ValidationException>(
            () => resolver.ResolveCityAsync(999_999, null, null, null, default));
        await Assert.ThrowsAsync<ValidationException>(
            () => resolver.ResolveCityAsync(null, mezzeh.Id, null, null, default));
    }

    [Fact(DisplayName = "A listing without a pin stores NULL coordinates; search by the English governorate name finds a listing stored in Arabic")]
    public async Task NullCoordinates_Persist_AndCitySearchMatchesEnglishGovernorateName()
    {
        var host = await _factory.SeedUserAsync($"ss-loc-{Guid.NewGuid():N}@test.local");
        var suffix = Guid.NewGuid().ToString("N")[..6];

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var accommodationType = await db.AccommodationTypes.FirstOrDefaultAsync();
        if (accommodationType is null)
        {
            accommodationType = AccommodationType.Create($"test-{Guid.NewGuid():N}"[..20], "شقة", "Apartment", "Residential", null, 1);
            db.AccommodationTypes.Add(accommodationType);
            await db.SaveChangesAsync();
        }

        var governorate = Governorate.Create($"لاذقية-{suffix}", $"Latakia-{suffix}", "SY");
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var located = ShortStayListing.Create(
            host.UserAccountId, accommodationType.Id, "شاليه", "وصف كافٍ للإعلان", 4, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), null, null);
        located.UpdateLocation(35.5m, 35.8m, governorate.Id, null, null, governorate.NameAr, LocationVisibility.Exact);
        var draft = ShortStayListing.Create(
            host.UserAccountId, accommodationType.Id, "مسودة", "وصف كافٍ للإعلان", 2, 1, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), null, null);

        foreach (var listing in new[] { located, draft })
        {
            var roomType = new RoomType { ShortStayListing = listing, Name = "Standard", BasePricePerNight = 50m };
            db.ShortStayListings.Add(listing);
            db.Add(roomType);
            db.Add(new AccommodationUnit { RoomType = roomType, Label = "A1" });
        }
        await db.SaveChangesAsync();
        located.Publish();
        await db.SaveChangesAsync();

        var stored = await db.ShortStayListings.AsNoTracking().SingleAsync(l => l.Id == draft.Id);
        Assert.Null(stored.Latitude);
        Assert.Null(stored.Longitude);

        var repository = new ShortStayListingRepository(db);
        var byEnglishName = await repository.SearchAsync(
            new ShortStayListingSearchFilter { City = $"latakia-{suffix}", Page = 1, PageSize = 20 });
        Assert.Equal(located.Id, Assert.Single(byEnglishName.Items).Id);

        var byArabicName = await repository.SearchAsync(
            new ShortStayListingSearchFilter { City = $"لاذقية-{suffix}", Page = 1, PageSize = 20 });
        Assert.Equal(located.Id, Assert.Single(byArabicName.Items).Id);
    }
}
