using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// The public list's unfiltered COUNT(*) reads the table unless a narrow index lets PostgreSQL
/// answer it from the index alone (audit B2: 1,464 buffers down to 23, 13 ms to 2.3 ms on 25,000
/// rows). Whether the planner then picks an index-only scan depends on table size and the
/// visibility map, so the stable thing to pin is that the migration really creates the partial
/// index the count predicate matches.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertyVisibleExpiresAtIndexTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Migrations create the partial ExpiresAt index over visible (published, not deleted) properties")]
    public async Task Migrations_CreateThePartialIndex()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var definitions = await db.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes " +
                "WHERE tablename = 'Properties' AND indexname = 'IX_Properties_Visible_ExpiresAt'")
            .ToListAsync();

        var definition = Assert.Single(definitions);
        Assert.Contains("(\"ExpiresAt\")", definition);
        Assert.Contains("NOT \"IsDeleted\"", definition);
        Assert.Contains("\"IsPublished\"", definition);

        // The existing index on the same column must still be there (the model keys the new one
        // by name; without that EF folds the two together and the migration drops the old one).
        var pendingWarning = await db.Database
            .SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes " +
                "WHERE tablename = 'Properties' AND indexname = 'IX_Properties_ExpiresAt_PendingWarning'")
            .ToListAsync();
        Assert.Single(pendingWarning);
    }
}
