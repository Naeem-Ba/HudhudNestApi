using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Listings.Commands.CreateProperty;
using HudhudNestApi.Application.Listings.Commands.DeleteProperty;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Enums;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.SocialDistribution;

/// <summary>
/// End-to-end proof for the Phase 1 audit's foundation fixes (see
/// "Claude outputs/تقرير-النشر-على-وسائل-التواصل-وخطة-الأتمتة-2026-09-25.md") — the real
/// <c>Program</c> host against real PostgreSQL, the real dispatch worker, only the platform APIs
/// and Cloudinary faked. Every existing SocialDistribution test exercises one handler against
/// Mocks in isolation and was fully green while the trigger (F-2), the fact validator (F-3), and
/// the SVG asset (F-4) were all broken in the real system — these tests are what would have caught
/// that: they drive the same paths the real app/worker drive, wired together for real.
/// </summary>
public sealed class SocialDistributionE2eTests : IClassFixture<SocialDistributionApiTestFactory>, IAsyncLifetime
{
    private readonly SocialDistributionApiTestFactory _factory;

    public SocialDistributionE2eTests(SocialDistributionApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Admin, HttpClient Owner, Guid OwnerId)> SeedUsersAsync()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var owner = await _factory.SeedUserAsync("owner");
        return (_factory.AuthedClient(admin.AccessToken), _factory.AuthedClient(owner.AccessToken), owner.Id);
    }

    private async Task<Guid> CreatePropertyAsync(
        Guid ownerId, ListingType listingType = ListingType.ForRent, bool publish = true, int imageCount = 2)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var governorate = await db.Governorates.OrderBy(g => g.Id).FirstAsync();
        var propertyType = await db.PropertyTypes.OrderBy(t => t.Id).FirstAsync();

        var isRent = listingType is ListingType.ForRent or ListingType.ForRentAndSale;
        var propertyId = await sender.Send(new CreatePropertyCommand(
            ownerId, "فيلا للإيجار في دمشق", "وصف كامل وتفصيلي لهذا العقار يشرح مساحته وموقعه بدقة",
            listingType, "شارع الثورة", "دمشق", null, "SY", null,
            governorate.Id, null, "المزة", null, null, propertyType.Id, null, null,
            listingType == ListingType.ForSale ? null : 500m, null,
            listingType is ListingType.ForSale or ListingType.ForRentAndSale ? 250000m : null,
            null, null, "USD", 4, 200m, 2, true, true, false, HeatingType.Central, null, null,
            LegalStatus: listingType is ListingType.ForSale or ListingType.ForRentAndSale ? LegalStatusType.GreenDeed : null,
            RentalStartDate: isRent ? DateOnly.FromDateTime(DateTime.UtcNow) : null,
            RentalEndDate: isRent ? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) : null,
            RentalDurationType: isRent ? RentalDurationType.OneYear : null));

        var property = await db.Properties.FirstAsync(p => p.Id == propertyId);
        for (var i = 0; i < imageCount; i++)
            db.Add(new Domain.Listings.Entities.PropertyImage { PropertyId = propertyId, Url = $"https://cdn.example.test/{i}.jpg", PublicId = $"p{i}", IsMain = i == 0 });
        if (!publish)
            property.Unpublish();
        await db.SaveChangesAsync();

        return propertyId;
    }

    /// <summary>
    /// This codebase's real create flow has no photos at the moment <c>CreatePropertyCommand</c>
    /// runs (image upload is a separate follow-up call) — so the synchronous
    /// <c>PropertyPublishedEvent</c> it raises is genuinely, correctly evaluated as ineligible
    /// (SocialDistributionEligibilityOptions.MinImageCount) even once <see cref="CreatePropertyAsync"/>
    /// has already added images to the SAME row (those images exist only after the command, and
    /// therefore after that first synchronous DistributionRun, returns). Backdating PublishedAt
    /// simulates the real elapsed time between "listing created + photos uploaded" and "the next
    /// reconciliation sweep runs" — at which point <see cref="IDistributionRunRepository.
    /// GetPublishedPropertyIdsWithoutRunAsync"/>'s retry-after-ineligible fix is what lets the
    /// listing be picked up at all, since it already has one (ineligible, zero-publication) run.
    /// </summary>
    private async Task BackdatePublishedAtAsync(Guid propertyId, TimeSpan by)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Properties\" SET \"PublishedAt\" = {DateTime.UtcNow.Subtract(by)} WHERE \"Id\" = {propertyId}");
    }

    // ── Trigger (F-2): a listing created through the app must be auto-distributed ──────────

    [Fact]
    public async Task PropertyCreatedThroughTheApp_IsAutomaticallyDistributed_OnceEligible()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);

        var propertyId = await CreatePropertyAsync(ownerId);
        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync();

        var publications = await _factory.GetPublicationsAsync(admin, propertyId);
        var publication = Assert.Single(publications);
        Assert.Equal("Published", publication.GetProperty("status").GetString());
        Assert.Single(_factory.Publishers[SocialPlatform.Telegram].Published);
    }

    [Fact]
    public async Task PropertyIneligibleAtCreationTime_HasNoPhotosYet_IsRetried_OncePhotosExistAndEnoughTimeHasPassed()
    {
        // The real create flow's own timing gap (photos are a separate, later upload call): the
        // synchronous PropertyPublishedEvent this listing raised at creation time was genuinely
        // ineligible (zero photos existed yet). A worker sweep run immediately afterwards must
        // still find nothing — this is the case IDistributionRunRepository.
        // GetPublishedPropertyIdsWithoutRunAsync's retry-after-ineligible fix exists for: without
        // it, that first ineligible run would have permanently excluded this listing.
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await CreatePropertyAsync(ownerId);

        await _factory.RunWorkerSweepAsync();
        Assert.Empty(await _factory.GetPublicationsAsync(admin, propertyId)); // too recent for reconciliation yet

        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync();

        Assert.Single(await _factory.GetPublicationsAsync(admin, propertyId));
    }

    [Fact]
    public async Task PropertyCreatedThroughTheApp_TelegramBody_ContainsTheRealCanonicalLink_NeverFallsBackToTheRawTemplate()
    {
        // Regression for F-3: the fact validator used to reject Facebook/Telegram bodies (which
        // include the link line) because the property GUID/UTM publication id look like a
        // fabricated price, silently degrading them to BuildDefaultBody's bare fact line.
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);

        var propertyId = await CreatePropertyAsync(ownerId);
        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync();

        var request = Assert.Single(_factory.Publishers[SocialPlatform.Telegram].Published);
        Assert.Contains(request.TargetUrl, request.Body, StringComparison.Ordinal);
        Assert.Contains("دمشق", request.Body); // F-6: PropertyType/Governorate now actually loaded
        Assert.DoesNotContain(".0000", request.Body); // F-7: no raw decimal(18,4) trailing zeros
    }

    [Fact]
    public async Task PropertyCreatedThroughTheApp_NeverGeneratesAnSvgAsset_PublishesWithTheRealPhoto()
    {
        // Regression for F-4: no target platform accepts image/svg+xml.
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Facebook);

        var propertyId = await CreatePropertyAsync(ownerId, listingType: ListingType.ForSale);
        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync();

        var request = Assert.Single(_factory.Publishers[SocialPlatform.Facebook].Published);
        Assert.EndsWith(".jpg", request.ImageUrl);
        Assert.Empty(_factory.Storage.Uploads); // the branded-asset generator was never invoked
    }

    [Fact]
    public async Task DraftPublishedViaPatchEndpoint_IsAlsoAutomaticallyDistributed()
    {
        // The original, pre-existing trigger path — still must work after the fix.
        var (admin, owner, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await CreatePropertyAsync(ownerId, publish: false);

        var response = await owner.PatchAsync($"/api/properties/{propertyId}/publish", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.RunWorkerSweepAsync();

        var publications = await _factory.GetPublicationsAsync(admin, propertyId);
        Assert.Single(publications);
    }

    // ── Reconciliation sweep — safety net for any path that raises no event ────────────────

    [Fact]
    public async Task ListingPublishedDirectlyInTheDatabase_WithNoEvent_IsPickedUpByReconciliation()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);

        // Bypasses CreatePropertyCommand entirely — no PropertyPublishedEvent is ever raised.
        // Backdated well past the reconciliation sweep's default 5-minute MinAge.
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        await _factory.RunWorkerSweepAsync();

        var publications = await _factory.GetPublicationsAsync(admin, propertyId);
        Assert.Single(publications);
    }

    [Fact]
    public async Task ReconciliationSweep_NeverReconsidersAPropertyOnceItHasARun()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        await _factory.RunWorkerSweepAsync();
        await _factory.RunWorkerSweepAsync();

        var publications = await _factory.GetPublicationsAsync(admin, propertyId);
        Assert.Single(publications); // not two
    }

    [Fact]
    public async Task ListingPublishedVeryRecently_IsNotYetReconciled_GivesTheRealEventAChanceFirst()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow);

        await _factory.RunWorkerSweepAsync();

        var publications = await _factory.GetPublicationsAsync(admin, propertyId);
        Assert.Empty(publications);
    }

    // ── Eligibility gate (F-11) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PropertyWithNoImages_IsNeverDistributed_EvenByReconciliation()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, imageCount: 0, publishedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        await _factory.RunWorkerSweepAsync();

        Assert.Empty(await _factory.GetPublicationsAsync(admin, propertyId));
    }

    // ── Lifecycle: status change / delete / expiry act on an already-Published post ────────

    [Fact]
    public async Task PropertyDeletedDirectly_WithNoStatusTransition_DeletesTheLivePost()
    {
        // Regression for F-10: deleting an Available listing never changes Status, so the
        // pre-existing PropertyStatusChangedEvent-only wiring would never have told
        // SocialDistribution about this at all.
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Facebook);
        var propertyId = await CreatePropertyAsync(ownerId, listingType: ListingType.ForSale);
        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync();
        Assert.Single(_factory.Publishers[SocialPlatform.Facebook].Published); // sanity: really went live

        using (var scope = _factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new DeletePropertyCommand(propertyId, ownerId, "127.0.0.1"));
        }

        Assert.Contains(_factory.Publishers[SocialPlatform.Facebook].LifecycleCalls, c => c.Action == "delete");
    }

    [Fact]
    public async Task ListingExpires_DeletesTheLivePost()
    {
        // Regression for F-10: ListingExpiryHostedService never told SocialDistribution a listing
        // expired at all.
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Facebook);
        var propertyId = await CreatePropertyAsync(ownerId, listingType: ListingType.ForSale);
        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync();
        Assert.Single(_factory.Publishers[SocialPlatform.Facebook].Published);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Properties\" SET \"ExpiresAt\" = {DateTime.UtcNow.AddDays(-1)} WHERE \"Id\" = {propertyId}");
        }

        await _factory.ExpiryWorker.RunOnceAsync(CancellationToken.None);

        Assert.Contains(_factory.Publishers[SocialPlatform.Facebook].LifecycleCalls, c => c.Action == "delete");
    }

    // ── Lease + reaper (F-8): a publication a crashed worker left stuck in Publishing ───────

    [Fact]
    public async Task PublicationStuckPastItsLease_IsReleasedByTheNextSweep_AsAnAmbiguousOutcomeDeadLetter_NeverRetried()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await CreatePropertyAsync(ownerId);
        await BackdatePublishedAtAsync(propertyId, TimeSpan.FromMinutes(10));
        await _factory.RunWorkerSweepAsync(); // real first sweep: distributes it, so there is a genuine Published-then-crashed row to reap

        // Simulate a worker that started publishing and then crashed before recording any
        // outcome — exactly the durable state PublishSocialPublicationCommandHandler's early
        // SaveChangesAsync now guarantees a real crash leaves behind.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publicationId = await db.SocialPublications.Where(p => p.PropertyId == propertyId).Select(p => p.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "SocialPublications"
                SET "Status" = 'Publishing', "StartedAt" = {DateTime.UtcNow.AddMinutes(-10)}, "LeaseUntil" = {DateTime.UtcNow.AddMinutes(-5)}
                WHERE "Id" = {publicationId}
                """);
        }

        await _factory.RunWorkerSweepAsync();

        var publication = Assert.Single(await _factory.GetPublicationsAsync(admin, propertyId));
        Assert.Equal("Failed", publication.GetProperty("status").GetString());
        Assert.Equal("AmbiguousOutcome", publication.GetProperty("errorCode").GetString());
        Assert.Single(_factory.Publishers[SocialPlatform.Telegram].Published); // the one real call before the simulated crash — never called a second time

        var deadLetters = await SocialDistributionApiTestFactory.SendAsync(admin.GetAsync("/api/social-distribution/dead-letters"));
        Assert.Equal(1, deadLetters.GetProperty("totalCount").GetInt32());
    }

    // ── Kill-switch / pause / credential expiry (Phase 6 operations) ─────────────────────────

    /// <summary>A manually created, queued publication for a freshly published listing — what a pause test needs: a Queued row the worker would pick up next sweep.</summary>
    private async Task<(Guid PublicationId, Guid PropertyId)> QueueManualPublicationAsync(HttpClient admin, Guid ownerId, Guid accountId)
    {
        // Published "now": inside reconciliation's MinAge window, so the sweep never also
        // auto-distributes it and muddies what these tests assert.
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow);
        var created = await SocialDistributionApiTestFactory.SendAsync(admin.PostAsJsonAsync("/api/social-distribution/publications", new
        {
            propertyId,
            socialAccountId = accountId,
            title = "عنوان",
            body = "نص يدوي للاختبار",
            imageUrl = (string?)null,
            hashtags = (string[]?)null,
            language = "ar",
        }));
        var publicationId = created.GetProperty("id").GetGuid();
        await SocialDistributionApiTestFactory.SendAsync(admin.PostAsJsonAsync($"/api/social-distribution/publications/{publicationId}/queue", new { scheduledAt = (DateTime?)null }));

        return (publicationId, propertyId);
    }

    private static async Task<string?> PublicationStatusAsync(HttpClient admin, Guid publicationId)
    {
        var publication = await SocialDistributionApiTestFactory.SendAsync(admin.GetAsync($"/api/social-distribution/publications/{publicationId}"));
        return publication.GetProperty("status").GetString();
    }

    [Fact]
    public async Task KillSwitch_StopsReconciliationAndDispatch_AndResumesCleanlyWhenReEnabled()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        _factory.Switch.IsEnabled = false;
        await _factory.RunWorkerSweepAsync();

        Assert.Empty(await _factory.GetPublicationsAsync(admin, propertyId)); // no publication was even created
        Assert.Empty(_factory.Publishers[SocialPlatform.Telegram].Published);

        _factory.Switch.IsEnabled = true;
        await _factory.RunWorkerSweepAsync();

        var publication = Assert.Single(await _factory.GetPublicationsAsync(admin, propertyId));
        Assert.Equal("Published", publication.GetProperty("status").GetString()); // nothing was lost while paused
    }

    [Fact]
    public async Task KillSwitch_LeavesAQueuedPublicationUntouched_AndRefusesManualPublishWith409()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        var accountId = await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var (publicationId, _) = await QueueManualPublicationAsync(admin, ownerId, accountId);

        _factory.Switch.IsEnabled = false;
        await _factory.RunWorkerSweepAsync();
        var manual = await admin.PostAsync($"/api/social-distribution/publications/{publicationId}/publish", null);

        Assert.Equal(HttpStatusCode.Conflict, manual.StatusCode);
        Assert.Equal("Queued", await PublicationStatusAsync(admin, publicationId));
        Assert.Empty(_factory.Publishers[SocialPlatform.Telegram].Published);

        _factory.Switch.IsEnabled = true;
        await _factory.RunWorkerSweepAsync();

        Assert.Equal("Published", await PublicationStatusAsync(admin, publicationId));
    }

    [Fact]
    public async Task DeactivatedChannel_PausesItsQueuedPublications_WithoutDeadLettersOrCalls_AndResumesOnReactivation()
    {
        // The runtime "pause one platform" control (no redeploy): the engine only checked the
        // channel when it CREATED a publication; the worker never looked at it again, so a
        // deactivated channel kept publishing everything already queued.
        var (admin, _, ownerId) = await SeedUsersAsync();
        var accountId = await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Telegram);
        var account = await SocialDistributionApiTestFactory.SendAsync(admin.GetAsync($"/api/social-distribution/accounts/{accountId}"));
        var channelId = account.GetProperty("socialChannelId").GetGuid();
        var (publicationId, _) = await QueueManualPublicationAsync(admin, ownerId, accountId);

        await SocialDistributionApiTestFactory.SendAsync(admin.PostAsync($"/api/social-distribution/channels/{channelId}/deactivate", null));
        await _factory.RunWorkerSweepAsync();

        Assert.Equal("Queued", await PublicationStatusAsync(admin, publicationId));
        Assert.Empty(_factory.Publishers[SocialPlatform.Telegram].Published);
        var deadLetters = await SocialDistributionApiTestFactory.SendAsync(admin.GetAsync("/api/social-distribution/dead-letters"));
        Assert.Equal(0, deadLetters.GetProperty("totalCount").GetInt32());

        await SocialDistributionApiTestFactory.SendAsync(admin.PostAsync($"/api/social-distribution/channels/{channelId}/activate", null));
        await _factory.RunWorkerSweepAsync();

        Assert.Equal("Published", await PublicationStatusAsync(admin, publicationId));
    }

    [Fact]
    public async Task RejectedCredential_ExpiresTheAccount_SoItIsTakenOutOfRotation()
    {
        var (admin, _, ownerId) = await SeedUsersAsync();
        var accountId = await _factory.ConfigurePlatformAsync(admin, SocialPlatform.Facebook);
        _factory.Publishers[SocialPlatform.Facebook].OnPublish = _ =>
            SocialPublishResult.Failure(SocialPublicationErrorCode.InvalidCredentials, "token rejected by the platform");
        var propertyId = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        await _factory.RunWorkerSweepAsync();

        var publication = Assert.Single(await _factory.GetPublicationsAsync(admin, propertyId));
        Assert.Equal("Failed", publication.GetProperty("status").GetString());
        var account = await SocialDistributionApiTestFactory.SendAsync(admin.GetAsync($"/api/social-distribution/accounts/{accountId}"));
        Assert.Equal("Expired", account.GetProperty("status").GetString());

        // A second listing now never even reaches the platform with the dead credential.
        var secondProperty = await _factory.SeedPublishedPropertyAsync(ownerId, publishedAtUtc: DateTime.UtcNow.AddMinutes(-10));
        await _factory.RunWorkerSweepAsync();
        Assert.Single(_factory.Publishers[SocialPlatform.Facebook].Published);
        Assert.Empty(await _factory.GetPublicationsAsync(admin, secondProperty)); // skipped by the engine: account is no longer Active
    }
}
