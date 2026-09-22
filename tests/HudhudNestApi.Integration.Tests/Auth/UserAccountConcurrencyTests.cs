using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// Exercises UserAccount's xmin concurrency token (RELEASE-BLOCKERS-AR.md B-9b) against a
/// real PostgreSQL server, mirroring PropertyConcurrencyTests (Listings) — see that file's
/// header for why this needs Postgres rather than the InMemory provider.
///
/// The scenario: UserAccount is one row several independent handlers write to without
/// coordinating (profile edits, plan selection, the agency-invitation accept/leave flow).
/// Before this token, a stale read racing a concurrent write silently succeeded regardless
/// of which columns either side touched, because EF only emits an UPDATE for the columns
/// the handler actually modified — there was nothing to detect the race at all.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Users")]
public sealed class UserAccountConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName =
        "Saving a UserAccount loaded before another writer's update throws DbUpdateConcurrencyException")]
    public async Task SaveChanges_OnAStaleRow_ThrowsConcurrencyException_InsteadOfSilentlyOverwriting()
    {
        var userId = await SeedUserAsync();

        // Two independent contexts loading the same account — e.g. the user editing their
        // profile in one tab while an agency invitation accept lands from another.
        await using var scopeA = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var userA = await dbA.UserAccounts.SingleAsync(u => u.Id == userId);

        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var userB = await dbB.UserAccounts.SingleAsync(u => u.Id == userId);

        userA.UpdateProfile("Updated", "Owner", displayName: null, DateTime.UtcNow);
        await dbA.SaveChangesAsync();

        // B's copy still carries the xmin value the row had before A's write.
        userB.UpdatePreferences("ar", "USD", "SY", DateTime.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => dbB.SaveChangesAsync());
    }

    [Fact(DisplayName = "Saving a UserAccount nobody else touched succeeds normally")]
    public async Task SaveChanges_WithNoConcurrentWriter_Succeeds()
    {
        var userId = await SeedUserAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.UserAccounts.SingleAsync(u => u.Id == userId);

        user.UpdatePreferences("ar", "USD", "SY", DateTime.UtcNow);

        // The xmin token must not turn every ordinary save into a conflict — only a save
        // that raced another writer should.
        await db.SaveChangesAsync();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await verifyDb.UserAccounts.SingleAsync(u => u.Id == userId);

        Assert.Equal("USD", reloaded.PreferredCurrency);
    }

    private async Task<Guid> SeedUserAsync()
    {
        // BUG-27: UserAccounts.Id carries FK_UserAccounts_Users_Id (a 1:1 shared-key
        // relationship with the ASP.NET Identity user) — creating a UserAccount directly
        // with a fresh Guid, with no matching Users row, violates that FK on real Postgres.
        // PostgresAuthTestFactory.SeedUserAsync creates both rows correctly.
        var seeded = await _factory.SeedUserAsync($"useraccount-concurrency-{Guid.NewGuid():N}@test.local");
        return seeded.UserAccountId;
    }
}
