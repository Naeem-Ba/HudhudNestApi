using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Growth;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Growth.Entities;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P4: the event log is idempotent, carries no personal data and is written by real flows.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class GrowthEventLogTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"ge{Guid.NewGuid():N}";
    private Guid _alice;
    private Guid _bob;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _alice = (await _factory.SeedUserAsync($"{_marker}a@test.local")).UserAccountId;
        _bob = (await _factory.SeedUserAsync($"{_marker}b@test.local")).UserAccountId;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "The same key is stored once; an unknown user id is skipped instead of failing")]
    public async Task Recorder_IsIdempotent_AndNeverThrows()
    {
        var key = $"favorite_added:{_marker}";
        using (var scope = _factory.Services.CreateScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<IGrowthEventRecorder>();
            await recorder.RecordAsync(GrowthEventNames.FavoriteAdded, key, _alice);
            await recorder.RecordAsync(GrowthEventNames.FavoriteAdded, key, _alice);
            await recorder.RecordAsync(GrowthEventNames.FavoriteAdded, key + ":ghost", Guid.NewGuid());
            await recorder.RecordAsync("not_a_known_event", key + ":bad", _alice);
        }

        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.GrowthEvents.CountAsync(e => e.IdempotencyKey.StartsWith(key)));
    }

    [Fact(DisplayName = "Registering keeps the UTM tags (plain tags only); saving a listing twice records one event")]
    public async Task Register_And_Favorite_WriteEvents()
    {
        var client = _factory.CreateClient();
        var email = $"{_marker}new@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Growth",
            lastName = "Test",
            email,
            password = "Tq7!vX2#mLp9$Rz4",
            privacyPolicyAccepted = true,
            privacyPolicyVersion = "test",
            termsAccepted = true,
            termsVersion = "test",
            acquisitionSource = "Facebook",
            acquisitionMedium = "paid social",
            acquisitionCampaign = "launch-2026",
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        Guid newUser;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            newUser = (await users.FindByEmailAsync(email))!.Id;
            var e = await db.GrowthEvents.SingleAsync(x => x.Name == GrowthEventNames.UserRegistered && x.UserId == newUser);
            Assert.Equal("facebook", e.Source);
            Assert.Null(e.Medium); // "paid social" is not a plain tag
            Assert.Equal("launch-2026", e.Campaign);
        }

        Guid listingId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var property = Property.Create($"{_marker} flat", "وصف كافٍ للإعلان", _bob, ListingType.ForRent, currencyCode: "USD");
            db.Properties.Add(property);
            await db.SaveChangesAsync();
            listingId = property.Id;
        }

        var alice = await ClientAsync(_alice);
        Assert.True((await alice.PostAsync($"/api/favorites/{listingId}", null)).IsSuccessStatusCode);
        await alice.DeleteAsync($"/api/favorites/{listingId}");
        Assert.True((await alice.PostAsync($"/api/favorites/{listingId}", null)).IsSuccessStatusCode);

        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await checkDb.GrowthEvents.CountAsync(e => e.Name == GrowthEventNames.FavoriteAdded && e.UserId == _alice && e.PropertyId == listingId));
    }

    private async Task<HttpClient> ClientAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("user");
        var roles = await users.GetRolesAsync(user);
        var token = tokens.GenerateAccessToken(new AccessTokenSubject(user.Id, user.Email, user.UserName, user.SecurityStamp), roles.ToArray());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
