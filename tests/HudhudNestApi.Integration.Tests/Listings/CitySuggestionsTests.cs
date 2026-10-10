using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// The city suggestions behind the filter's City box, on a real server.
///
/// A listing's City is filled from its governorate's name in whatever language the publisher used,
/// so the same governorate is stored as "دمشق" by one listing and "Damascus" by another. Suggestions
/// for a chosen governorate therefore list that governorate's own names plus the cities found on its
/// listings, de-duplicated, and never another governorate's.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class CitySuggestionsTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"city-{Guid.NewGuid():N}"[..13];

    private Governorate _damascus = null!;
    private Governorate _ruralDamascus = null!;

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "City suggestions scoped to a governorate list its own names and its listings' cities, de-duplicated, and nothing of other governorates")]
    public async Task CitySuggestions_AreScopedToTheGovernorate()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var lookups = scope.ServiceProvider.GetRequiredService<ICommonLookupService>();

        var damascus = (await lookups.GetCitiesByGovernorateAsync(_damascus.Id)).Select(c => c.Name).ToArray();
        Assert.Contains(_damascus.NameAr, damascus);
        Assert.Contains(_damascus.NameEn, damascus);
        Assert.Contains($"Jaramana-{_marker}", damascus);
        Assert.DoesNotContain(_ruralDamascus.NameAr, damascus);
        // "Damascus-x" stored by two listings and the governorate's own name is one suggestion, not three.
        Assert.Single(damascus, c => string.Equals(c, _damascus.NameEn, StringComparison.OrdinalIgnoreCase));

        var rural = (await lookups.GetCitiesByGovernorateAsync(_ruralDamascus.Id)).Select(c => c.Name).ToArray();
        Assert.DoesNotContain($"Jaramana-{_marker}", rural);

        Assert.Empty(await lookups.GetCitiesByGovernorateAsync(int.MaxValue));
    }

    [Fact(DisplayName = "GET /api/lookups/cities?governorateId= answers with that governorate's cities; without it, every city")]
    public async Task CitiesEndpoint_ScopesByGovernorateId()
    {
        await SeedAsync();
        using var client = _factory.CreateClient();

        var scoped = await client.GetFromJsonAsync<LookupItemDto[]>($"/api/lookups/cities?governorateId={_ruralDamascus.Id}");
        Assert.Contains(scoped!, c => c.Name == _ruralDamascus.NameAr);
        Assert.DoesNotContain(scoped!, c => c.Name == _damascus.NameAr);

        var all = await client.GetFromJsonAsync<LookupItemDto[]>("/api/lookups/cities");
        Assert.Contains(all!, c => c.Name == _ruralDamascus.NameAr);
        Assert.Contains(all!, c => c.Name == _damascus.NameAr);

        // A non-positive id is not a scope: it behaves like no filter instead of failing.
        var zero = await client.GetFromJsonAsync<LookupItemDto[]>("/api/lookups/cities?governorateId=0");
        Assert.Equal(all!.Length, zero!.Length);
    }

    private async Task SeedAsync()
    {
        var owner = await _factory.SeedUserAsync($"{_marker}@test.local");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Names built like the real pair: the second contains the first, in Arabic and in English.
        _damascus = Governorate.Create($"دمشق-{suffix}", $"Damascus-{suffix}");
        _ruralDamascus = Governorate.Create($"ريف دمشق-{suffix}", $"Rural Damascus-{suffix}");
        db.Governorates.AddRange(_damascus, _ruralDamascus);
        await db.SaveChangesAsync();

        void Add(string name, string city, int? governorateId)
        {
            var property = Property.Create($"{_marker} {name}", "وصف كافٍ للإعلان", owner.UserAccountId, ListingType.ForSale);
            property.City = city;
            property.GovernorateId = governorateId;
            property.PurchasePrice = 1000m;
            db.Properties.Add(property);
        }

        Add("ar", _damascus.NameAr, _damascus.Id);
        Add("en", _damascus.NameEn, _damascus.Id);
        Add("rural", _ruralDamascus.NameAr, _ruralDamascus.Id);
        Add("jaramana", $"Jaramana-{_marker}", _damascus.Id);
        await db.SaveChangesAsync();
    }
}
