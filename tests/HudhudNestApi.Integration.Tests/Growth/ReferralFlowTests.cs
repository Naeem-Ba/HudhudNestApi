using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Referrals.Entities;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Referrals;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P5: invite → sign-up with code → real activity → qualified; fraud checks; privacy.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class ReferralFlowTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"rf{Guid.NewGuid():N}";
    private Guid _referrer;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _referrer = (await _factory.SeedUserAsync($"{_marker}r@test.local")).UserAccountId;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "A code from a link attaches at sign-up; a listing live 7 days plus a confirmed phone qualifies it once")]
    public async Task Invite_SignUp_Qualify()
    {
        var referrer = await ClientAsync(_referrer);
        var me = await JsonOf(await referrer.GetAsync("/api/referrals/me"));
        var code = me.GetProperty("code").GetString()!;
        Assert.Equal(code, (await JsonOf(await referrer.GetAsync("/api/referrals/me"))).GetProperty("code").GetString());

        var invited = await RegisterAsync("i1", code.ToLowerInvariant());
        Assert.Equal(1, (await JsonOf(await referrer.GetAsync("/api/referrals/me"))).GetProperty("pending").GetInt32());

        // Nothing done yet: stays pending.
        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(ReferralStatus.Pending, (await LoadAsync(invited)).Status);

        await ConfirmPhoneAsync(invited, "h1" + _marker);
        await PublishListingAsync(invited, DateTime.UtcNow.AddDays(-8));

        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);
        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(ReferralStatus.Qualified, (await LoadAsync(invited)).Status);
        Assert.Equal(1, await NotificationsAsync(_referrer, NotificationType.ReferralQualified));

        var text = await (await referrer.GetAsync("/api/referrals/me")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(invited.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@test.local", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "A banned invited account is rejected; an unknown code records nothing; a fresh listing does not qualify; idle ones expire")]
    public async Task FraudChecks()
    {
        var referrer = await ClientAsync(_referrer);
        var code = (await JsonOf(await referrer.GetAsync("/api/referrals/me"))).GetProperty("code").GetString()!;

        var banned = await RegisterAsync("banned", code);
        await ConfirmPhoneAsync(banned, "hb" + _marker);
        await PublishListingAsync(banned, DateTime.UtcNow.AddDays(-30));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == banned)).IsBanned = true;
            await db.SaveChangesAsync();
        }

        var fresh = await RegisterAsync("fresh", code);
        await ConfirmPhoneAsync(fresh, "hf" + _marker);
        await PublishListingAsync(fresh, DateTime.UtcNow.AddDays(-1));

        var idle = await RegisterAsync("idle", code);
        var stranger = await RegisterAsync("nobody", "ZZZZZZZZ");

        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);

        var bannedRow = await LoadAsync(banned);
        Assert.Equal(ReferralStatus.Rejected, bannedRow.Status);
        Assert.Equal("invited_account_inactive", bannedRow.Reason);
        Assert.Equal(ReferralStatus.Pending, (await LoadAsync(fresh)).Status);
        Assert.Equal(ReferralStatus.Pending, (await LoadAsync(idle)).Status);
        Assert.Null(await TryLoadAsync(stranger));

        // After the 90-day window the account that did nothing expires.
        await Sweeper().RunOnceAsync(DateTime.UtcNow.AddDays(91), CancellationToken.None);
        Assert.Equal(ReferralStatus.Expired, (await LoadAsync(idle)).Status);
    }

    [Fact(DisplayName = "Admin endpoints are admin-only; a reward can be recorded only on a qualified referral")]
    public async Task AdminOnly()
    {
        var referrer = await ClientAsync(_referrer);
        Assert.Equal(HttpStatusCode.Forbidden, (await referrer.GetAsync("/api/admin/referrals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await referrer.PostAsync($"/api/admin/referrals/{Guid.NewGuid()}/rewarded", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/referrals/me")).StatusCode);
    }

    private async Task<Guid> RegisterAsync(string suffix, string code)
    {
        var email = $"{_marker}{suffix}@test.local";
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Invited",
            lastName = "Person",
            email,
            password = "Tq7!vX2#mLp9$Rz4",
            privacyPolicyAccepted = true,
            privacyPolicyVersion = "test",
            termsAccepted = true,
            termsVersion = "test",
            referralCode = code,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }

    private async Task ConfirmPhoneAsync(Guid userId, string lookupHash)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.PhoneNumberConfirmed = true;
        user.PhoneNumberLookupHash = lookupHash;
        await db.SaveChangesAsync();
    }

    private async Task PublishListingAsync(Guid ownerId, DateTime publishedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = Property.Create($"{_marker} listing", "وصف كافٍ للإعلان", ownerId, ListingType.ForSale, currencyCode: "USD");
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        db.Entry(property).Property(nameof(Property.PublishedAt)).CurrentValue = publishedAt;
        await db.SaveChangesAsync();
    }

    private async Task<Referral?> TryLoadAsync(Guid referred)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Referrals.AsNoTracking().SingleOrDefaultAsync(r => r.ReferredUserId == referred);
    }

    private async Task<Referral> LoadAsync(Guid referred) => (await TryLoadAsync(referred))!;

    private async Task<int> NotificationsAsync(Guid recipient, NotificationType type)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Notifications.CountAsync(n => n.RecipientId == recipient && n.Type == type);
    }

    private ReferralQualificationHostedService Sweeper() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<ReferralQualificationHostedService>>());

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

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
