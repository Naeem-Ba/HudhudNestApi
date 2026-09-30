using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Infrastructure.Lookups;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Nothing used to check that a listing's district belongs to its governorate (or its neighborhood to its
/// district), so a listing could be filed under the wrong governorate and appear in the wrong filter results.
/// Runs on a real server.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class LocationHierarchyCheckerTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Consistent ids pass; a district or neighborhood from elsewhere, or an unknown id, fails")]
    public async Task ChecksEveryPresentPair()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var checker = new LocationHierarchyChecker(db);

        // Own rows: the test database is not seeded with the location catalog.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var governorate = Governorate.Create($"م-{suffix}", $"G-{suffix}");
        var otherGovernorate = Governorate.Create($"م2-{suffix}", $"G2-{suffix}");
        db.Governorates.AddRange(governorate, otherGovernorate);
        await db.SaveChangesAsync();
        var district = District.Create(governorate.Id, $"ح-{suffix}", $"D-{suffix}");
        var otherDistrict = District.Create(governorate.Id, $"ح2-{suffix}", $"D2-{suffix}");
        db.Districts.AddRange(district, otherDistrict);
        await db.SaveChangesAsync();
        var neighborhood = Neighborhood.Create(district.Id, $"ن-{suffix}");
        db.Neighborhoods.Add(neighborhood);
        await db.SaveChangesAsync();
        var otherGovernorateId = otherGovernorate.Id;
        var otherDistrictId = otherDistrict.Id;

        Assert.True(await checker.IsConsistentAsync(district.GovernorateId, district.Id, neighborhood.Id));
        Assert.True(await checker.IsConsistentAsync(district.GovernorateId, null, null)); // district typed as text
        Assert.True(await checker.IsConsistentAsync(district.GovernorateId, district.Id, null));

        Assert.False(await checker.IsConsistentAsync(otherGovernorateId, district.Id, null));
        Assert.False(await checker.IsConsistentAsync(null, otherDistrictId, neighborhood.Id)); // neighborhood of another district
        Assert.False(await checker.IsConsistentAsync(district.GovernorateId, district.Id, int.MaxValue));
    }
}
