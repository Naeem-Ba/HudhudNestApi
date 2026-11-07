using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Infrastructure.Plans;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Plans;

/// <summary>
/// An office whose owner's plan ended more than 30 days ago disappears from the public, reversibly; during the grace period
/// and after a renewal its listings are public. Over real Postgres, driving the sweep directly.
/// </summary>
[Trait("Feature", "PaymentRequests")]
public sealed class AgencyPlanSuspensionTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();
    private SeededUser _admin = null!;
    private SeededUser _owner = null!;
    private SeededUser _member = null!;
    private Guid _agencyId;
    private Guid _ownerListingId;
    private Guid _memberListingId;
    private Guid _otherReasonListingId;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        await _factory.InScopeAsync(services => DatabaseSeeder.SeedAsync(services));
        await _factory.InScopeAsync(services => services.GetRequiredService<ICommonLookupService>().RefreshAllAsync());
        _admin = await _factory.SeedUserAsync("aps-admin", RoleNames.Admin);
        _owner = await _factory.SeedUserAsync("aps-owner");
        _member = await _factory.SeedUserAsync("aps-member");

        var adminClient = _factory.AuthedClient(_admin.AccessToken);
        var activate = await adminClient.PostAsJsonAsync(
            $"/api/admin/users/{_owner.Id}/subscription/activate", new { tier = "basic", durationDays = 30 });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var agency = Agency.Create("Suspension Office", "suspension-office", _owner.Id, "SY", now);
            db.Agencies.Add(agency);
            _agencyId = agency.Id;
            await db.SaveChangesAsync();

            var members = await db.UserAccounts.Where(a => a.Id == _owner.Id || a.Id == _member.Id).ToListAsync();
            foreach (var account in members) account.JoinAgency(agency.Id, now);

            var ownerListing = Property.Create("Owner flat", "d", _owner.Id, ListingType.ForRent);
            ownerListing.SetAgency(agency.Id);
            var memberListing = Property.Create("Member flat", "d", _member.Id, ListingType.ForRent);
            memberListing.SetAgency(agency.Id);
            var otherReason = Property.Create("Moderated flat", "d", _member.Id, ListingType.ForRent);
            otherReason.SetAgency(agency.Id);
            otherReason.TakeDown(_admin.Id, "Spam");
            db.Properties.AddRange(ownerListing, memberListing, otherReason);
            await db.SaveChangesAsync();

            _ownerListingId = ownerListing.Id;
            _memberListingId = memberListing.Id;
            _otherReasonListingId = otherReason.Id;
        });
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private Task SweepAsync() => _factory.InScopeAsync(async services =>
    {
        var service = new AgencyPlanSuspensionHostedService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgencyPlanSuspensionHostedService>.Instance);
        await service.RunOnceAsync(CancellationToken.None);
    });

    private Task SetPlanEndAsync(DateTime end) => _factory.InScopeAsync(async services =>
        await services.GetRequiredService<AppDbContext>().UserAccounts.Where(a => a.Id == _owner.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PlanExpiresAt, end)));

    private Task<(bool Owner, bool Member, string? OtherReason)> StateAsync() => _factory.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        var rows = await db.Properties.AsNoTracking()
            .Where(p => p.Id == _ownerListingId || p.Id == _memberListingId || p.Id == _otherReasonListingId)
            .ToListAsync();
        return (
            rows.Single(p => p.Id == _ownerListingId).IsPublished,
            rows.Single(p => p.Id == _memberListingId).IsPublished,
            rows.Single(p => p.Id == _otherReasonListingId).TakenDownReason);
    });

    [Fact]
    public async Task ListingsStayPublicDuringGrace_DisappearAfterIt_AndReturnOnRenewal()
    {
        // Grace period: expired 10 days ago -> nothing hidden.
        await SetPlanEndAsync(DateTime.UtcNow.AddDays(-10));
        await SweepAsync();
        var grace = await StateAsync();
        Assert.True(grace.Owner);
        Assert.True(grace.Member);

        // Expired 31 days ago -> owner's and member's listings hidden; the other take-down keeps its own reason.
        await SetPlanEndAsync(DateTime.UtcNow.AddDays(-31));
        await SweepAsync();
        await SweepAsync();
        var hidden = await StateAsync();
        Assert.False(hidden.Owner);
        Assert.False(hidden.Member);
        Assert.Equal("Spam", hidden.OtherReason);

        // Renewed by an administrator -> back at once, with no sweep in between; the moderated one stays down.
        var renew = await _factory.AuthedClient(_admin.AccessToken).PostAsJsonAsync(
            $"/api/admin/users/{_owner.Id}/subscription/activate", new { tier = "basic", durationDays = 30 });
        Assert.Equal(HttpStatusCode.OK, renew.StatusCode);
        var back = await StateAsync();
        Assert.True(back.Owner);
        Assert.True(back.Member);
        Assert.Equal("Spam", back.OtherReason);
    }

    [Fact]
    public async Task TheDailySweepAlsoLiftsTheHiding_WhenTheEndDateIsMovedForward()
    {
        await SetPlanEndAsync(DateTime.UtcNow.AddDays(-31));
        await SweepAsync();
        Assert.False((await StateAsync()).Owner);

        await SetPlanEndAsync(DateTime.UtcNow.AddDays(30));
        await SweepAsync();
        var back = await StateAsync();
        Assert.True(back.Owner);
        Assert.True(back.Member);
        Assert.Equal("Spam", back.OtherReason);
    }
}
