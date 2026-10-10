using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// Phone confirmation audit 2026-10-10: typing a phone number that another account already holds into
/// the profile must be a 409, whether or not that account has a <c>PhoneNumberLookupHash</c>.
///
/// The duplicate pre-check in <c>SetPhoneNumberAsync</c> compared only the lookup hash, but the hash is
/// filled in by a separate, optional backfill (and phone registration did not write it until the
/// proof-beats-claim change). A holder without a hash was therefore invisible to the check, the save hit
/// the unique index on <c>NormalizedPhoneNumber</c> and the caller got a 500. The in-memory test host
/// does not enforce unique indexes, so this needs real Postgres.
/// </summary>
[Collection("AuthPostgres")]
public sealed class PhoneNumberTakenPostgresTests
{
    private const string Password = "StrongPass!123";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Profile_PhoneNumberHeldByAnAccountWithoutALookupHash_IsAConflict(bool holderConfirmed)
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();
        var phone = $"+49{Math.Abs(Guid.NewGuid().GetHashCode() % 1_000_000_000L) + 1_000_000_000L:D10}";

        var holder = await factory.SeedUserAsync($"holder-{Guid.NewGuid():N}@example.com");
        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            // The state of every account whose hash was never backfilled.
            await db.Users.Where(x => x.Id == holder.IdentityId).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.PhoneNumber, phone)
                .SetProperty(x => x.NormalizedPhoneNumber, phone)
                .SetProperty(x => x.PhoneNumberLookupHash, (string?)null)
                .SetProperty(x => x.PhoneNumberConfirmed, holderConfirmed));
        });

        var caller = await factory.SeedUserAsync($"caller-{Guid.NewGuid():N}@example.com");
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = caller.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var access = JsonDocument.Parse(await login.Content.ReadAsStringAsync())
            .RootElement.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me")
        {
            Content = JsonContent.Create(new { phoneNumber = phone })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"Expected 409 Conflict but got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
