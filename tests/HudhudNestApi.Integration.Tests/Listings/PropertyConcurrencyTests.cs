using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Exercises Property's new xmin concurrency token (RELEASE-BLOCKERS-AR.md B-9) against a
/// real PostgreSQL server. This cannot be a plain unit test: xmin is a value the server
/// assigns and advances on every UPDATE, and EF Core's InMemory provider (used by
/// TestApplication for the rest of this assembly's fast tests) has no such column at all —
/// only a real Postgres connection can prove the second writer actually loses the race
/// instead of silently overwriting the first.
///
/// Reuses PostgresAuthTestFactory rather than building a parallel real-Postgres host: it
/// already does exactly what this needs (migrate, truncate, serialize via the AuthPostgres
/// collection) and nothing here is auth-specific about that plumbing.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertyConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName =
        "Saving a Property loaded before another writer's update throws DbUpdateConcurrencyException")]
    public async Task SaveChanges_OnAStaleRow_ThrowsConcurrencyException_InsteadOfSilentlyOverwriting()
    {
        var propertyId = await SeedPropertyAsync();

        // Two independent contexts loading the same row — the shape of two concurrent
        // requests (two browser tabs, or a background sweep racing an owner's edit).
        await using var scopeA = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var propertyA = await dbA.Properties.SingleAsync(p => p.Id == propertyId);

        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var propertyB = await dbB.Properties.SingleAsync(p => p.Id == propertyId);

        propertyA.ColdRent = 1500m;
        await dbA.SaveChangesAsync();

        // B's copy still carries the xmin value the row had before A's write — Postgres
        // will have advanced the real one, so B's UPDATE ... WHERE xmin = @old matches
        // zero rows.
        propertyB.ColdRent = 1600m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => dbB.SaveChangesAsync());
    }

    [Fact(DisplayName = "Saving a Property nobody else touched succeeds normally")]
    public async Task SaveChanges_WithNoConcurrentWriter_Succeeds()
    {
        var propertyId = await SeedPropertyAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = await db.Properties.SingleAsync(p => p.Id == propertyId);

        property.ColdRent = 1700m;

        // The xmin token must not turn every ordinary save into a conflict — only a save
        // that raced another writer should.
        await db.SaveChangesAsync();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await verifyDb.Properties.SingleAsync(p => p.Id == propertyId);

        Assert.Equal(1700m, reloaded.ColdRent);
    }

    private async Task<Guid> SeedPropertyAsync()
    {
        // BUG-27: UserAccounts.Id carries FK_UserAccounts_Users_Id (a 1:1 shared-key
        // relationship with the ASP.NET Identity user) — creating a UserAccount directly
        // with a fresh Guid, with no matching Users row, violates that FK on real Postgres.
        // The InMemory provider used by this assembly's other (fast) tests doesn't enforce
        // FK constraints, so this only ever surfaced here, against a real server.
        // PostgresAuthTestFactory.SeedUserAsync creates both rows correctly.
        var owner = await _factory.SeedUserAsync($"property-concurrency-{Guid.NewGuid():N}@test.local");
        var ownerId = owner.UserAccountId;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create(
            "شقة للإيجار",
            "وصف كافٍ للإعلان",
            ownerId,
            ListingType.ForRent);

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        return property.Id;
    }
}
