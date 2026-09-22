using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Users;

/// <summary>
/// Regression coverage for a real bug found during a release-readiness audit (2026-09-07):
/// <see cref="HudhudNestApi.Infrastructure.Repositories.UserAccountRepository.GetByIdAsync"/> used
/// to query with <c>.AsNoTracking()</c>, silently defeating every "load, mutate, SaveChangesAsync"
/// handler built on top of <c>IUserAccountRepository</c> (UpdateUserCommandHandler,
/// DeleteUserCommandHandler, and others) — the HTTP call succeeded (204) but the database row
/// never changed. A mocked-repository unit test cannot catch this class of bug by construction
/// (the mock returns the same in-memory object it was told to return); only a real round trip
/// through the actual HTTP pipeline against a real Postgres database, re-read from a fresh
/// DbContext scope, proves persistence actually happened. Keep these tests real-DB-backed.
/// </summary>
public sealed class UserProfilePersistenceTests : IAsyncLifetime
{
    private const string SeededPassword = "StrongPass!123";

    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "DELETE /api/Users/me actually persists the deletion schedule to the database")]
    public async Task DeleteAccount_PersistsDeletionSchedule()
    {
        // Finding F7 (docs/DATABASE-PRODUCTION-READINESS.md): DELETE /api/Users/me now
        // schedules deletion (delay window) instead of anonymizing synchronously -- this test
        // was originally written against the old immediate-anonymization behavior; updated to
        // assert the new contract while keeping its original purpose (a real HTTP-to-Postgres
        // round trip, re-read from a fresh scope, proves persistence actually happened). The
        // sweep's own execution behavior is covered separately by AccountDeletionSweepTests.
        var user = await _factory.SeedUserAsync("profile-persistence-delete");

        using var client = _factory.AuthedClient(user.AccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/Users/me")
        {
            Content = JsonContent.Create(new { currentPassword = SeededPassword })
        };
        var response = await client.SendAsync(request);

        var body = response.IsSuccessStatusCode ? string.Empty : await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"Expected 202, got {response.StatusCode}: {body}");

        await _factory.InScopeAsync(async services =>
        {
            // A fresh scope/DbContext — never the one the handler used — so this can only
            // pass if the change actually reached the database, not a first-level cache.
            var db = services.GetRequiredService<AppDbContext>();
            var reloaded = await db.UserAccounts
                .AsNoTracking()
                .SingleAsync(a => a.Id == user.Id);

            // Not anonymized yet -- only the sweep does that, once ScheduledFor arrives.
            Assert.False(reloaded.IsDeleted);
            Assert.Equal("Test", reloaded.FirstName);
            Assert.True(reloaded.HasPendingDeletionRequest);
            Assert.NotNull(reloaded.DeletionScheduledFor);
        });
    }

    [Fact(DisplayName = "PUT /api/Users/me actually persists profile field changes to the database")]
    public async Task UpdateProfile_PersistsChanges()
    {
        var user = await _factory.SeedUserAsync("profile-persistence-update");

        using var client = _factory.AuthedClient(user.AccessToken);
        var response = await client.PutAsJsonAsync("/api/Users/me", new
        {
            firstName = "ChangedFirstName",
            lastName = "ChangedLastName",
            displayName = (string?)null,
            phoneNumber = (string?)null,
            bio = "changed bio",
            contactInfo = (string?)null
        });

        var body = response.IsSuccessStatusCode ? string.Empty : await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204, got {response.StatusCode}: {body}");

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var reloaded = await db.UserAccounts
                .AsNoTracking()
                .SingleAsync(a => a.Id == user.Id);

            Assert.Equal("ChangedFirstName", reloaded.FirstName);
            Assert.Equal("ChangedLastName", reloaded.LastName);
            Assert.Equal("changed bio", reloaded.Bio);
        });
    }
}
