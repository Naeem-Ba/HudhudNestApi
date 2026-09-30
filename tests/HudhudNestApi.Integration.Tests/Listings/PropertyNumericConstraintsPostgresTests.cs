using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Real-Postgres regression coverage for Finding F4 (docs/DATABASE-PRODUCTION-READINESS.md):
/// Area, Rooms, ColdRent, WarmRent, and PurchasePrice must each reject a zero/negative value at
/// the database level, while staying nullable (Area is legitimately optional except for Land
/// listings; the three price columns are legitimately null depending on ListingType). Every
/// assertion here writes directly through <see cref="Property"/>'s public setters and
/// <c>SaveChangesAsync</c> -- bypassing the application/domain validation layer entirely -- the
/// same technique Phase 2's original audit used to prove Property.Rooms/Area/Price had no
/// database-level backstop before this migration existed.
/// </summary>
public sealed class PropertyNumericConstraintsPostgresTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        var owner = await _factory.SeedUserAsync("numeric-constraints-owner");
        _ownerId = owner.Id;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public static IEnumerable<object[]> RejectedValues() =>
        new[] { new object[] { 0m }, new object[] { -1m }, new object[] { -0.01m } };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task Area_ZeroOrNegative_IsRejected(decimal value) =>
        await AssertRejectedAsync(p => p.Area = value);

    [Fact]
    public async Task Area_Null_IsAccepted() =>
        await AssertAcceptedAsync(p => p.Area = null);

    [Fact]
    public async Task Area_Positive_IsAccepted() =>
        await AssertAcceptedAsync(p => p.Area = 42.5m);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rooms_ZeroOrNegative_IsRejected(int value) =>
        await AssertRejectedAsync(p => p.Rooms = value);

    [Fact]
    public async Task Rooms_Null_IsAccepted() =>
        await AssertAcceptedAsync(p => p.Rooms = null);

    [Fact]
    public async Task Rooms_Positive_IsAccepted() =>
        await AssertAcceptedAsync(p => p.Rooms = 3);

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task ColdRent_ZeroOrNegative_IsRejected(decimal value) =>
        await AssertRejectedAsync(p => p.ColdRent = value);

    [Fact]
    public async Task ColdRent_NullOrPositive_IsAccepted()
    {
        await AssertAcceptedAsync(p => p.ColdRent = null);
        await AssertAcceptedAsync(p => p.ColdRent = 500m);
    }

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task WarmRent_ZeroOrNegative_IsRejected(decimal value) =>
        await AssertRejectedAsync(p => p.WarmRent = value);

    [Fact]
    public async Task WarmRent_NullOrPositive_IsAccepted()
    {
        await AssertAcceptedAsync(p => p.WarmRent = null);
        await AssertAcceptedAsync(p => p.WarmRent = 650m);
    }

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task PurchasePrice_ZeroOrNegative_IsRejected(decimal value) =>
        await AssertRejectedAsync(p => p.PurchasePrice = value);

    [Fact]
    public async Task PurchasePrice_NullOrPositive_IsAccepted()
    {
        await AssertAcceptedAsync(p => p.PurchasePrice = null);
        await AssertAcceptedAsync(p => p.PurchasePrice = 150_000m);
    }

    private async Task AssertRejectedAsync(Action<Property> mutate) =>
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = NewProperty();
            mutate(property);
            db.Properties.Add(property);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());

            Assert.Contains(
                "23514",
                exception.InnerException?.Message ?? exception.Message,
                StringComparison.Ordinal);
        });

    private async Task AssertAcceptedAsync(Action<Property> mutate) =>
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = NewProperty();
            mutate(property);
            db.Properties.Add(property);

            // Must not throw.
            await db.SaveChangesAsync();

            db.Entry(property).State = EntityState.Detached;
        });

    private Property NewProperty() =>
        Property.Create(
            $"Test Property {Guid.NewGuid():N}",
            "Integration test property for Finding F4.",
            _ownerId,
            ListingType.ForRent);
}
