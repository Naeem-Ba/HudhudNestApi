using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Valuation.Commands.SubmitValuationContactConsent;
using HudhudNestApi.Application.Valuation.Queries.GetMyAgencyValuationInquiries;
using HudhudNestApi.Application.Valuation.Queries.GetValuationInquiryStatus;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;

namespace HudhudNestApi.Integration.Tests.Valuation;

/// <summary>
/// Stage 9 — the explicit "test the office's API response; it must fail if phone/email/contact
/// info is found before consent" requirement, run end-to-end against a real PostgreSQL server
/// through the real MediatR pipeline (SubmitValuationContactConsentCommand,
/// GetMyAgencyValuationInquiriesQuery, GetValuationInquiryStatusQuery) rather than mocks — the
/// same "don't rely only on mocked/UI tests" rigor Stage 8's AdminValuationDashboardIntegrationTests
/// applies. Mirrors that file's PostgresAuthTestFactory/InScopeAsync shape.
/// </summary>
[Collection("AuthPostgres")]
public sealed class ValuationContactConsentIntegrationTests
{
    [Fact]
    public async Task OfficeDashboard_NeverShowsContactInfo_UntilConsentIsSubmitted_ThenShowsExactlyWhatWasConsented()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var now = DateTime.UtcNow;
        var owner = await factory.SeedUserAsync($"office-{Guid.NewGuid():N}@example.test");

        Guid agencyId = default, inquiryId = default, invitationId = default;

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var governorate = Governorate.Create("دمشق", "Damascus");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();

            var agency = Agency.Create("مكتب الاختبار", $"test-office-{Guid.NewGuid():N}", owner.IdentityId, "SY", now);
            db.Agencies.Add(agency);
            agencyId = agency.Id;

            // Agency.OwnerUserId and UserAccount.AgencyId are two separate facts (see Agency's
            // own doc comment, and ValuationApiTestFactory.SeedAgencyWithOwnerAsync's) --
            // GetMyAgencyValuationInquiriesQueryHandler resolves "my agency" from the latter, so
            // the owner's own UserAccount row must join the agency too, not just own it.
            var ownerAccount = await db.UserAccounts.FirstAsync(a => a.Id == owner.IdentityId);
            ownerAccount.JoinAgency(agencyId, now);
            db.UserAccounts.Update(ownerAccount);

            var inquiry = ValuationInquiry.Create(governorate.Id, ListingType.ForSale, now);
            inquiryId = inquiry.Id;
            inquiry.MarkAwaitingOfficeResponses(now);

            var invitation = ValuationOfficeInvitation.Create(agencyId, inquiry.Id, ValuationMatchLevel.Neighborhood, now);
            invitation.MarkResponded(now);
            invitationId = invitation.Id;

            var response = ValuationOfficeResponse.Create(invitation.Id, 150_000m, now, "تقدير أولي");

            db.ValuationInquiries.Add(inquiry);
            db.ValuationOfficeInvitations.Add(invitation);
            db.ValuationOfficeResponses.Add(response);

            await db.SaveChangesAsync();
        });

        // ── Before consent: the office's own dashboard API response must carry no phone,
        // no email, no contact information whatsoever. ──
        var beforeConsent = await factory.InScopeAsync(async services =>
        {
            var mediator = services.GetRequiredService<IMediator>();
            return await mediator.Send(new GetMyAgencyValuationInquiriesQuery(owner.IdentityId));
        });

        var rowBefore = Assert.Single(beforeConsent);
        Assert.Null(rowBefore.CustomerContactPhone);
        Assert.Null(rowBefore.CustomerContactEmail);

        // Also assert on the raw serialized JSON shape a real HTTP response would carry, not
        // just the in-memory DTO -- guards against a future serializer/naming-policy change
        // silently smuggling the value through under a different key.
        var jsonBefore = System.Text.Json.JsonSerializer.Serialize(rowBefore);
        Assert.DoesNotContain("0999", jsonBefore); // no fragment of the phone we'll submit next
        Assert.DoesNotContain("@example.test", jsonBefore);

        // ── The customer submits explicit consent, through the real command pipeline. ──
        var consentResult = await factory.InScopeAsync(async services =>
        {
            var mediator = services.GetRequiredService<IMediator>();
            return await mediator.Send(new SubmitValuationContactConsentCommand(
                inquiryId, invitationId, ActorUserId: null,
                ContactPhone: "0999112233", ContactEmail: "customer@example.test"));
        });

        Assert.Equal(invitationId, consentResult.InvitationId);

        // ── After consent: the office's dashboard now carries exactly what was consented. ──
        var afterConsent = await factory.InScopeAsync(async services =>
        {
            var mediator = services.GetRequiredService<IMediator>();
            return await mediator.Send(new GetMyAgencyValuationInquiriesQuery(owner.IdentityId));
        });

        var rowAfter = Assert.Single(afterConsent);
        Assert.Equal("0999112233", rowAfter.CustomerContactPhone);
        Assert.Equal("customer@example.test", rowAfter.CustomerContactEmail);

        // ── The customer's own status view reflects the consent too. ──
        var status = await factory.InScopeAsync(async services =>
        {
            var mediator = services.GetRequiredService<IMediator>();
            return await mediator.Send(new GetValuationInquiryStatusQuery(inquiryId, ActorUserId: null));
        });

        var estimate = Assert.Single(status.Estimates);
        Assert.True(estimate.ContactConsentGiven);
        Assert.Equal("مكتب الاختبار", estimate.AgencyName);

        // Idempotency: submitting again must not create a second row nor change the timestamp.
        var secondSubmit = await factory.InScopeAsync(async services =>
        {
            var mediator = services.GetRequiredService<IMediator>();
            return await mediator.Send(new SubmitValuationContactConsentCommand(
                inquiryId, invitationId, ActorUserId: null,
                ContactPhone: "0000000000", ContactEmail: "different@example.test"));
        });

        // Not Assert.Equal: PostgreSQL "timestamp" columns are microsecond-precision while .NET
        // DateTime is tick-precision (100ns), so a value that round-trips through Postgres loses
        // its last decimal digit -- consentResult.ConsentedAt (the in-process value returned by
        // the first call) and secondSubmit.ConsentedAt (read back from the row on the idempotent
        // second call) can differ by a fraction of a microsecond despite being the same instant.
        // What this test actually asserts -- the timestamp did not change -- only needs a
        // tolerance far tighter than any real re-submission could produce.
        Assert.True(
            (consentResult.ConsentedAt - secondSubmit.ConsentedAt).Duration() < TimeSpan.FromMilliseconds(1),
            $"Expected ConsentedAt to stay the same on a repeat submit, but it moved from {consentResult.ConsentedAt:O} to {secondSubmit.ConsentedAt:O}.");

        var consentRowCount = await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            return await db.ValuationContactConsents.CountAsync(c => c.InvitationId == invitationId);
        });

        Assert.Equal(1, consentRowCount);
    }

    [Fact]
    public async Task SubmitContactConsent_BeforeAnyResponseExists_IsRejected()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        var now = DateTime.UtcNow;
        var owner = await factory.SeedUserAsync($"office-{Guid.NewGuid():N}@example.test");
        Guid inquiryId = default, invitationId = default;

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var governorate = Governorate.Create("حلب", "Aleppo");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();

            // ValuationOfficeInvitations.AgencyId is a required FK to Agencies -- this scenario
            // doesn't care which office was invited, only that no response exists yet, but the
            // invitation still needs a real Agency row to point at.
            var agency = Agency.Create("مكتب اختبار آخر", $"test-office-{Guid.NewGuid():N}", owner.IdentityId, "SY", now);
            db.Agencies.Add(agency);

            var inquiry = ValuationInquiry.Create(governorate.Id, ListingType.ForRent, now);
            inquiryId = inquiry.Id;
            inquiry.MarkAwaitingOfficeResponses(now);

            var invitation = ValuationOfficeInvitation.Create(agency.Id, inquiry.Id, ValuationMatchLevel.District, now);
            invitationId = invitation.Id; // still Sent -- no response yet

            db.ValuationInquiries.Add(inquiry);
            db.ValuationOfficeInvitations.Add(invitation);
            await db.SaveChangesAsync();
        });

        await factory.InScopeAsync(async services =>
        {
            var mediator = services.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<Application.Common.Exceptions.ConflictException>(() => mediator.Send(
                new SubmitValuationContactConsentCommand(inquiryId, invitationId, null, "0991234567", null)));
        });
    }
}
