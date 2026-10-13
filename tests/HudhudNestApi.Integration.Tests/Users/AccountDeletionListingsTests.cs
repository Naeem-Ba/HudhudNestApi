using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Users.Commands.DeleteUser;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Users;

/// <summary>
/// Owner rule (2026-10-13): when an account is deleted, a member of a real-estate office keeps their listings
/// and they move to the office owner (so inquiries reach the owner); someone outside any office leaves their
/// listings untouched to run out on their normal expiry. Runs the real handler on real Postgres.
/// </summary>
public sealed class AccountDeletionListingsTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> SeedListingAsync(Guid ownerId, string title, Guid? agentId = null) =>
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = Property.Create(title, "وصف العقار", ownerId, ListingType.ForRent);
            if (agentId is not null) property.AgentId = agentId;
            db.Properties.Add(property);
            await db.SaveChangesAsync(CancellationToken.None);
            return property.Id;
        });

    private async Task<(Guid OwnerId, Guid OwnerListing)> LoadAsync(Guid propertyId) =>
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var p = await db.Properties.AsNoTracking().SingleAsync(x => x.Id == propertyId);
            return (p.OwnerId, p.AgentId ?? Guid.Empty);
        });

    [Fact(DisplayName = "Deleting an office member hands their listings to the office owner; others' listings stay put")]
    public async Task OfficeMember_ListingsMoveToTheOfficeOwner()
    {
        var owner = await _factory.SeedUserAsync("del-office-owner");
        var member = await _factory.SeedUserAsync("del-office-member");
        var bystander = await _factory.SeedUserAsync("del-bystander");

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var agency = Agency.Create("Office", $"office-{Guid.NewGuid():N}", owner.Id, "SY", now);
            db.Agencies.Add(agency);
            await db.SaveChangesAsync(CancellationToken.None);
            var account = await db.UserAccounts.SingleAsync(a => a.Id == member.Id);
            account.JoinAgency(agency.Id, now);
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var memberListing = await SeedListingAsync(member.Id, "Member flat", agentId: member.Id);
        var bystanderListing = await SeedListingAsync(bystander.Id, "Bystander flat");

        var result = await _factory.InScopeAsync(services =>
            services.GetRequiredService<DeleteUserCommandHandler>().ExecuteScheduledDeletionAsync(member.Id, CancellationToken.None));
        Assert.True(result.Success, string.Join(",", result.Errors));

        var moved = await LoadAsync(memberListing);
        Assert.Equal(owner.Id, moved.OwnerId);
        Assert.Equal(owner.Id, moved.OwnerListing); // the responsible agent was the member, so it follows the listing
        Assert.Equal(bystander.Id, (await LoadAsync(bystanderListing)).OwnerId);
    }

    [Fact(DisplayName = "Deleting a user outside any office leaves their listings with them")]
    public async Task NonMember_ListingsStayUntouched()
    {
        var loner = await _factory.SeedUserAsync("del-loner");
        var listing = await SeedListingAsync(loner.Id, "Loner flat");

        var result = await _factory.InScopeAsync(services =>
            services.GetRequiredService<DeleteUserCommandHandler>().ExecuteScheduledDeletionAsync(loner.Id, CancellationToken.None));
        Assert.True(result.Success, string.Join(",", result.Errors));

        Assert.Equal(loner.Id, (await LoadAsync(listing)).OwnerId);
    }
}
