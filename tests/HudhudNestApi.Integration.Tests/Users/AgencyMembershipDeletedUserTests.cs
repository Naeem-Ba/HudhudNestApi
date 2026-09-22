using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Users;

/// <summary>
/// Real-Postgres regression coverage for Finding F2
/// (docs/DATABASE-PRODUCTION-READINESS.md): a deleted/anonymized <c>UserAccount</c> must not
/// keep counting toward or appearing in an agency's member list.
/// <see cref="HudhudNestApi.Infrastructure.Repositories.AgencyRepository"/> queries
/// <c>UserAccounts</c> directly (not through the paired Identity row), so this is the one
/// confirmed live gap the Phase 2 audit found -- everything else (public profile pages, the
/// admin dashboard) already excludes/labels a deleted account correctly by checking the
/// Identity-side <c>IsDeleted</c> flag instead.
/// </summary>
public sealed class AgencyMembershipDeletedUserTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "GetMembersAsync/CountMembersAsync exclude a deleted (anonymized) member")]
    public async Task DeletedMember_ExcludedFromListAndCount()
    {
        var activeMember = await _factory.SeedUserAsync("agency-active-member");
        var deletedMember = await _factory.SeedUserAsync("agency-deleted-member");
        var owner = await _factory.SeedUserAsync("agency-owner");

        var agencyId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var agency = Agency.Create(
                "Test Agency",
                $"test-agency-{Guid.NewGuid():N}",
                owner.Id,
                "SY",
                now);

            db.Agencies.Add(agency);
            await db.SaveChangesAsync(CancellationToken.None);

            foreach (var memberId in new[] { activeMember.Id, deletedMember.Id })
            {
                var account = await db.UserAccounts.SingleAsync(a => a.Id == memberId);
                account.JoinAgency(agency.Id, now);
            }

            // Anonymize() is the exact mutation DeleteUserCommandHandler performs -- exercising
            // it directly here (rather than via the HTTP delete endpoint, which also requires
            // password re-authentication plumbing unrelated to this test) keeps the test
            // focused on the repository behavior Finding F2 is about.
            var deletedAccount = await db.UserAccounts.SingleAsync(a => a.Id == deletedMember.Id);
            deletedAccount.Anonymize(now);

            await db.SaveChangesAsync(CancellationToken.None);

            return agency.Id;
        });

        await _factory.InScopeAsync(async services =>
        {
            var agencies = services.GetRequiredService<IAgencyRepository>();

            var members = await agencies.GetMembersAsync(agencyId, CancellationToken.None);
            var count = await agencies.CountMembersAsync(agencyId, CancellationToken.None);

            Assert.Single(members);
            Assert.Equal(activeMember.Id, members[0].Id);
            Assert.DoesNotContain(members, m => m.Id == deletedMember.Id);
            Assert.Equal(1, count);
        });
    }

    [Fact(DisplayName = "GetMembersAsync/CountMembersAsync still include every active member")]
    public async Task ActiveMembers_AllCountedAndListed()
    {
        var owner = await _factory.SeedUserAsync("agency-owner-active-only");
        var memberA = await _factory.SeedUserAsync("agency-member-a");
        var memberB = await _factory.SeedUserAsync("agency-member-b");

        var agencyId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var agency = Agency.Create(
                "Another Test Agency",
                $"test-agency-{Guid.NewGuid():N}",
                owner.Id,
                "SY",
                now);

            db.Agencies.Add(agency);
            await db.SaveChangesAsync(CancellationToken.None);

            foreach (var memberId in new[] { memberA.Id, memberB.Id })
            {
                var account = await db.UserAccounts.SingleAsync(a => a.Id == memberId);
                account.JoinAgency(agency.Id, now);
            }

            await db.SaveChangesAsync(CancellationToken.None);

            return agency.Id;
        });

        await _factory.InScopeAsync(async services =>
        {
            var agencies = services.GetRequiredService<IAgencyRepository>();

            var members = await agencies.GetMembersAsync(agencyId, CancellationToken.None);
            var count = await agencies.CountMembersAsync(agencyId, CancellationToken.None);

            Assert.Equal(2, members.Count);
            Assert.Equal(2, count);
        });
    }
}
