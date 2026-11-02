using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Growth.Entities;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Growth;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P10: the activity rollup and the admin growth overview, on a past day no other test uses.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class GrowthDashboardTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"gd{Guid.NewGuid():N}"[..12];
    private Guid _a;
    private Guid _b;
    private Guid _c;
    private Guid _admin;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _a = (await _factory.SeedUserAsync($"{_marker}a@test.local")).UserAccountId;
        _b = (await _factory.SeedUserAsync($"{_marker}b@test.local")).UserAccountId;
        _c = (await _factory.SeedUserAsync($"{_marker}c@test.local")).UserAccountId;
        _admin = (await _factory.SeedUserAsync($"{_marker}x@test.local")).UserAccountId;
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(_admin.ToString()))!, RoleNames.Admin)).Succeeded);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Rollup writes one active day per account per day, once, from sign-ins and refreshes")]
    public async Task Rollup_IsIdempotent()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
                db.RefreshTokens.Add(new RefreshToken { UserId = _a, TokenHash = $"{_marker}{i}", CreatedAt = DateTime.UtcNow.AddMinutes(-i), ExpiresAt = DateTime.UtcNow.AddDays(7) });
            await db.SaveChangesAsync();
        }

        await Rollup().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);
        await Rollup().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);

        using var check = _factory.Services.CreateScope();
        var events = await check.ServiceProvider.GetRequiredService<AppDbContext>().Set<GrowthEvent>()
            .Where(e => e.UserId == _a && e.Name == GrowthEventNames.UserActive).ToListAsync();
        Assert.Single(events);
    }

    [Fact(DisplayName = "Overview: funnel, campaigns and retention follow the written definitions; admin only")]
    public async Task Overview_FollowsTheDefinitions()
    {
        // A day in 2021 picked per run, so nothing else in the shared database falls into the window.
        var d0 = new DateTime(2021, 1, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(Random.Shared.Next(0, 300));
        var campaign = $"camp-{_marker}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "UserAccounts" SET "CreatedAt" = {0} WHERE "Id" IN ({1}, {2}, {3})""", d0, _a, _b, _c);
            void Add(string name, Guid user, DateTime at, string? source = null, string? camp = null) =>
                db.Set<GrowthEvent>().Add(GrowthEvent.Create(name, $"{name}:{user}:{at:yyyyMMddHH}:{_marker}", at, user, source: source, campaign: camp));
            Add(GrowthEventNames.UserRegistered, _a, d0, "facebook", campaign);
            Add(GrowthEventNames.UserRegistered, _b, d0, "facebook", campaign);
            Add(GrowthEventNames.UserRegistered, _c, d0);
            Add(GrowthEventNames.ListingPublished, _a, d0.AddDays(2));
            Add(GrowthEventNames.ContactStarted, _b, d0.AddDays(40)); // after the 30-day window: not "activated"
            Add(GrowthEventNames.UserActive, _c, d0);
            Add(GrowthEventNames.UserActive, _a, d0.AddDays(1));
            Add(GrowthEventNames.UserActive, _a, d0.AddDays(8));
            Add(GrowthEventNames.UserActive, _b, d0.AddDays(31));
            await db.SaveChangesAsync();
        }

        var from = d0.Date;
        var to = d0.Date.AddDays(1);
        var url = $"/api/admin/growth/overview?fromUtc={from:yyyy-MM-ddTHH:mm:ssZ}&toUtc={to:yyyy-MM-ddTHH:mm:ssZ}";

        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync(_a)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(url)).StatusCode);

        var admin = await ClientAsync(_admin);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.GetAsync($"/api/admin/growth/overview?fromUtc={to:yyyy-MM-dd}&toUtc={from:yyyy-MM-dd}")).StatusCode);

        var response = await admin.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var root = JsonDocument.Parse(body).RootElement;

        Assert.Equal(3, root.GetProperty("totals").GetProperty("newAccounts").GetInt32());
        var funnel = root.GetProperty("funnel");
        Assert.Equal(3, funnel.GetProperty("registered").GetInt32());
        Assert.Equal(1, funnel.GetProperty("activated").GetInt32());
        Assert.Equal(1, funnel.GetProperty("published").GetInt32());
        Assert.Equal(0, funnel.GetProperty("contacted").GetInt32());

        var row = Assert.Single(root.GetProperty("campaigns").EnumerateArray());
        Assert.Equal(campaign, row.GetProperty("campaign").GetString());
        Assert.Equal("facebook", row.GetProperty("source").GetString());
        Assert.Equal(2, row.GetProperty("registered").GetInt32());
        Assert.Equal(1, row.GetProperty("activated").GetInt32());
        Assert.Equal(1, root.GetProperty("untaggedRegistrations").GetInt32());

        var retention = root.GetProperty("retention").EnumerateArray()
            .ToDictionary(r => r.GetProperty("days").GetInt32(), r => (r.GetProperty("cohort").GetInt32(), r.GetProperty("retained").GetInt32()));
        Assert.Equal((3, 2), retention[1]);
        Assert.Equal((3, 2), retention[7]);
        Assert.Equal((3, 1), retention[30]);

        var day = Assert.Single(root.GetProperty("days").EnumerateArray());
        Assert.Equal(3, day.GetProperty("newAccounts").GetInt32());
        Assert.Equal(1, day.GetProperty("activeUsers").GetInt32());
        Assert.DoesNotContain(_marker + "a@", body);
        Assert.DoesNotContain(_a.ToString(), body);
    }

    private UserActivityRollupHostedService Rollup() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<UserActivityRollupHostedService>>());

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
