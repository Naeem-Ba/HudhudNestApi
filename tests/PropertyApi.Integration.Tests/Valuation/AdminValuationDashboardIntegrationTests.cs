using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Lookups.Entities;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.Auth;

namespace PropertyApi.Integration.Tests.Valuation;

/// <summary>
/// Stage 8 (Admin Dashboard) — the explicit "database data == dashboard statistics" test this
/// stage requires, run against a real PostgreSQL server (never a mock repository): rows are
/// inserted directly through AppDbContext, then the real AdminValuationInquiryService (resolved
/// from the same DI container ValuationInquiriesController would use) is asked to compute
/// statistics over them, and its numbers are checked against what was actually seeded — not
/// against a hand-rolled expectation independent of the schema.
///
/// Mirrors PhoneNumberLookupHashBackfillPostgresTests' shape: PostgresAuthTestFactory for a
/// real Postgres-backed WebApplicationFactory, InScopeAsync to seed via AppDbContext directly,
/// a second InScopeAsync to read back through the real service.
/// </summary>
[Collection("AuthPostgres")]
public sealed class AdminValuationDashboardIntegrationTests
{
    [Fact]
    public async Task GetOfficeStatisticsAsync_MatchesHandSeededInvitationsAndResponses()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var now = DateTime.UtcNow;

        var ownerA = await factory.SeedUserAsync(UniqueEmail("office-a"));
        var ownerB = await factory.SeedUserAsync(UniqueEmail("office-b"));

        Guid agencyAId = default, agencyBId = default;

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var governorate = Governorate.Create("دمشق", "Damascus");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();

            var agencyA = Agency.Create("مكتب الأول", $"office-a-{Guid.NewGuid():N}", ownerA.IdentityId, "SY", now);
            var agencyB = Agency.Create("مكتب الثاني", $"office-b-{Guid.NewGuid():N}", ownerB.IdentityId, "SY", now);
            db.Agencies.AddRange(agencyA, agencyB);
            agencyAId = agencyA.Id;
            agencyBId = agencyB.Id;

            // ── Agency A: 4 invitations ──────────────────────────────
            // 1) Responded, submitted before its inquiry's deadline (within SLA).
            var inquiry1 = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            var invitation1 = ValuationOfficeInvitation.Create(agencyAId, inquiry1.Id, ValuationMatchLevel.Neighborhood, now);
            invitation1.MarkResponded(now.AddHours(1));
            var response1 = ValuationOfficeResponse.Create(invitation1.Id, 100_000m, now.AddHours(1));

            // 2) Responded, but submitted AFTER its inquiry's deadline — a deliberately
            // constructed "late" row (this stage's Integration test's whole point is proving
            // the SLA filter actually discriminates by timestamp rather than assuming every
            // Responded invitation counts; see AdminValuationInquiryService's own doc comment
            // for why the write-path handler never lets this occur through the real API).
            var inquiry2 = ValuationInquiry.Create(governorate.Id, ListingType.ForRent, now);
            var invitation2 = ValuationOfficeInvitation.Create(agencyAId, inquiry2.Id, ValuationMatchLevel.District, now);
            invitation2.MarkResponded(inquiry2.ExpiresAt.AddHours(2));
            var response2 = ValuationOfficeResponse.Create(invitation2.Id, 120_000m, inquiry2.ExpiresAt.AddHours(2));

            // 3) Still Sent — no response yet.
            var inquiry3 = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            var invitation3 = ValuationOfficeInvitation.Create(agencyAId, inquiry3.Id, ValuationMatchLevel.Governorate, now);

            // 4) Expired — never responded.
            var inquiry4 = ValuationInquiry.Create(governorate.Id, ListingType.ForRent, now);
            var invitation4 = ValuationOfficeInvitation.Create(agencyAId, inquiry4.Id, ValuationMatchLevel.Neighborhood, now);
            invitation4.Expire(now.AddHours(25));

            // ── Agency B: 2 invitations, neither ever responded ─────
            var inquiry5 = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            var invitation5 = ValuationOfficeInvitation.Create(agencyBId, inquiry5.Id, ValuationMatchLevel.Neighborhood, now);

            var inquiry6 = ValuationInquiry.Create(governorate.Id, ListingType.ForRent, now);
            var invitation6 = ValuationOfficeInvitation.Create(agencyBId, inquiry6.Id, ValuationMatchLevel.Neighborhood, now);

            db.ValuationInquiries.AddRange(inquiry1, inquiry2, inquiry3, inquiry4, inquiry5, inquiry6);
            db.ValuationOfficeInvitations.AddRange(invitation1, invitation2, invitation3, invitation4, invitation5, invitation6);
            db.ValuationOfficeResponses.AddRange(response1, response2);

            await db.SaveChangesAsync();
        });

        var stats = await factory.InScopeAsync(async services =>
        {
            var service = services.GetRequiredService<IAdminValuationInquiryService>();
            return await service.GetOfficeStatisticsAsync(CancellationToken.None);
        });

        var officeA = Assert.Single(stats, s => s.AgencyId == agencyAId);
        Assert.Equal("مكتب الأول", officeA.AgencyName);
        Assert.Equal(4, officeA.TotalInvitations);
        Assert.Equal(2, officeA.TotalResponses);
        // Only response1 was submitted at/before its inquiry's ExpiresAt.
        Assert.Equal(1, officeA.ResponsesWithinSla);
        Assert.Equal(0.5m, officeA.ResponseRate);
        Assert.Equal(0.25m, officeA.SlaComplianceRate);
        // The whole point of this stage's warning: these must NOT be the same number here.
        Assert.NotEqual(officeA.ResponseRate, officeA.SlaComplianceRate);
        Assert.False(officeA.RequiresManualReview);

        var officeB = Assert.Single(stats, s => s.AgencyId == agencyBId);
        Assert.Equal("مكتب الثاني", officeB.AgencyName);
        Assert.Equal(2, officeB.TotalInvitations);
        Assert.Equal(0, officeB.TotalResponses);
        Assert.Equal(0, officeB.ResponsesWithinSla);
        Assert.Equal(0m, officeB.ResponseRate);
        Assert.Equal(0m, officeB.SlaComplianceRate);
    }

    [Fact]
    public async Task FlagOfficeForReviewAsync_PersistsToRealDatabase_AndAppearsInStatistics()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var owner = await factory.SeedUserAsync(UniqueEmail("slow-office"));
        var admin = await factory.SeedUserAsync(UniqueEmail("admin"));
        var now = DateTime.UtcNow;

        Guid agencyId = default;

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var governorate = Governorate.Create("حلب", "Aleppo");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();

            var agency = Agency.Create("مكتب بطيء", $"slow-office-{Guid.NewGuid():N}", owner.IdentityId, "SY", now);
            db.Agencies.Add(agency);
            agencyId = agency.Id;

            var inquiry = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            var invitation = ValuationOfficeInvitation.Create(agencyId, inquiry.Id, ValuationMatchLevel.Neighborhood, now);

            db.ValuationInquiries.Add(inquiry);
            db.ValuationOfficeInvitations.Add(invitation);

            await db.SaveChangesAsync();
        });

        var flagResult = await factory.InScopeAsync(async services =>
        {
            var service = services.GetRequiredService<IAdminValuationInquiryService>();
            // performedByUserId flows straight into AuditLogService.LogAsync, and
            // AuditLogs.UserId is a required FK to UserAccounts -- it must be a real seeded
            // user, not an arbitrary Guid.
            return await service.FlagOfficeForReviewAsync(
                agencyId, "بطء متكرر في الرد", admin.IdentityId, "127.0.0.1", CancellationToken.None);
        });

        Assert.True(flagResult.Succeeded);

        // Reload through a brand-new scope (new AppDbContext instance) to prove the write was
        // actually committed to Postgres, not just mutated on an in-memory tracked instance.
        var persistedAgency = await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            return await db.Agencies.FindAsync([agencyId], CancellationToken.None);
        });

        Assert.NotNull(persistedAgency);
        Assert.True(persistedAgency!.RequiresManualReview);
        Assert.Equal("بطء متكرر في الرد", persistedAgency.ManualReviewReason);

        var stats = await factory.InScopeAsync(async services =>
        {
            var service = services.GetRequiredService<IAdminValuationInquiryService>();
            return await service.GetOfficeStatisticsAsync(CancellationToken.None);
        });

        var officeRow = Assert.Single(stats);
        Assert.True(officeRow.RequiresManualReview);
        Assert.Equal("بطء متكرر في الرد", officeRow.ManualReviewReason);
    }

    [Fact]
    public async Task GetInquiriesAsync_StatusFilter_MatchesRawDatabaseCount()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var now = DateTime.UtcNow;

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var governorate = Governorate.Create("حمص", "Homs");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();

            var pending1 = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            var pending2 = ValuationInquiry.Create(governorate.Id, ListingType.ForRent, now);

            var completed = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            completed.MarkMatchedFromListings(now);
            completed.MarkCompleted(now);

            db.ValuationInquiries.AddRange(pending1, pending2, completed);
            await db.SaveChangesAsync();
        });

        var (pagedTotal, rawCount) = await factory.InScopeAsync(async services =>
        {
            var service = services.GetRequiredService<IAdminValuationInquiryService>();
            var db = services.GetRequiredService<AppDbContext>();

            var page = await service.GetInquiriesAsync(1, 20, "Pending", CancellationToken.None);
            var raw = await db.ValuationInquiries.CountAsync(i => i.Status == ValuationInquiryStatus.Pending);

            return (page, raw);
        });

        Assert.Equal(rawCount, pagedTotal.TotalCount);
        Assert.Equal(2, pagedTotal.TotalCount);
        Assert.All(pagedTotal.Items, i => Assert.Equal("Pending", i.Status));
    }

    private static string UniqueEmail(string label)
        => $"{label}-{Guid.NewGuid():N}@example.test";
}
