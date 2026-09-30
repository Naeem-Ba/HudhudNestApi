using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialPublicationTests
{
    private static SocialPublication MakePublication(SocialPlatform platform = SocialPlatform.Facebook) =>
        SocialPublication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), platform);

    private static SocialPublication MakePublicationWithContent(SocialPlatform platform = SocialPlatform.Facebook)
    {
        var publication = MakePublication(platform);
        var content = SocialPostContent.Create(
            publication.Id, platform, "عنوان", "نص المنشور",
            "https://cdn.example.com/img.jpg", "https://realestateworld.world/properties/p1", null, "ar");
        publication.AttachContent(content);
        return publication;
    }

    // ── Creation / UTM defaults ─────────────────────────────────────────────

    [Fact]
    public void Create_StartsAsDraft_WithUtmDefaultsSet()
    {
        var publication = MakePublication(SocialPlatform.Facebook);

        Assert.Equal(SocialPublicationStatus.Draft, publication.Status);
        Assert.Equal("facebook", publication.UtmSource);
        Assert.Equal("social", publication.UtmMedium);
        Assert.Equal("social_distribution", publication.UtmCampaign);
        Assert.Equal($"publication_{publication.Id}", publication.UtmContent);
        Assert.Equal(0, publication.RetryCount);
        Assert.Equal(SocialPublication.DefaultMaxRetryCount, publication.MaxRetryCount);
    }

    [Fact]
    public void Create_DifferentPlatforms_ProduceDistinctUtmSource()
    {
        Assert.Equal("instagram", MakePublication(SocialPlatform.Instagram).UtmSource);
        Assert.Equal("telegram", MakePublication(SocialPlatform.Telegram).UtmSource);
        Assert.Equal("tiktok", MakePublication(SocialPlatform.TikTok).UtmSource);
        Assert.Equal("youtube", MakePublication(SocialPlatform.YouTube).UtmSource);
        Assert.Equal("linkedin", MakePublication(SocialPlatform.LinkedIn).UtmSource);
    }

    [Fact]
    public void Create_EmptyPropertyId_Throws() =>
        Assert.Throws<DomainException>(() => SocialPublication.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook));

    [Fact]
    public void Create_EmptySocialAccountId_Throws() =>
        Assert.Throws<DomainException>(() => SocialPublication.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), SocialPlatform.Facebook));

    [Fact]
    public void Create_PromotionalRepost_UsesDistinctCampaignAndPropertyScopedContent()
    {
        var propertyId = Guid.NewGuid();
        var publication = SocialPublication.Create(propertyId, Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, isPromotionalRepost: true);

        Assert.Equal("property_promotion", publication.UtmCampaign);
        Assert.Equal($"promotion_{propertyId}", publication.UtmContent);
    }

    [Fact]
    public void Create_NonRepost_KeepsOriginalCampaignAndPerPublicationContent()
    {
        var publication = MakePublication();

        Assert.Equal("social_distribution", publication.UtmCampaign);
        Assert.Equal($"publication_{publication.Id}", publication.UtmContent);
    }

    // ── Queue ────────────────────────────────────────────────────────────────

    [Fact]
    public void Queue_WithoutContent_Throws()
    {
        var publication = MakePublication();
        Assert.Throws<InvalidStateTransitionException>(() => publication.Queue(null, DateTime.UtcNow));
    }

    [Fact]
    public void Queue_WithContent_MovesToQueued()
    {
        var publication = MakePublicationWithContent();

        publication.Queue(null, DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Queued, publication.Status);
        Assert.True(publication.IsDueToPublish(DateTime.UtcNow));
    }

    [Fact]
    public void Queue_ContentPendingReview_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Content!.RequireReview("بانتظار مراجعة بشرية.");

        Assert.Throws<InvalidStateTransitionException>(() => publication.Queue(null, DateTime.UtcNow));
    }

    [Fact]
    public void Queue_ContentRejected_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Content!.RequireReview("test");
        publication.Content.Reject(Guid.NewGuid(), "غير مناسب.", DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.Queue(null, DateTime.UtcNow));
    }

    [Fact]
    public void Queue_ContentApprovedAfterReview_Succeeds()
    {
        var publication = MakePublicationWithContent();
        publication.Content!.RequireReview("test");
        publication.Content.Approve(Guid.NewGuid(), DateTime.UtcNow);

        publication.Queue(null, DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Queued, publication.Status);
    }

    [Fact]
    public void Queue_ScheduledInThePast_Throws()
    {
        var publication = MakePublicationWithContent();
        var now = DateTime.UtcNow;

        Assert.Throws<DomainException>(() => publication.Queue(now.AddMinutes(-5), now));
    }

    [Fact]
    public void Queue_ScheduledInTheFuture_IsNotYetDue()
    {
        var publication = MakePublicationWithContent();
        var now = DateTime.UtcNow;

        publication.Queue(now.AddHours(1), now);

        Assert.False(publication.IsDueToPublish(now));
        Assert.True(publication.IsDueToPublish(now.AddHours(2)));
    }

    // ── Reschedule (Phase 12) ───────────────────────────────────────────────

    [Fact]
    public void Reschedule_WhileQueued_ChangesScheduledAt()
    {
        var publication = MakePublicationWithContent();
        var now = DateTime.UtcNow;
        publication.Queue(now.AddHours(1), now);

        publication.Reschedule(now.AddHours(5), now);

        Assert.Equal(now.AddHours(5), publication.ScheduledAt);
        Assert.Equal(SocialPublicationStatus.Queued, publication.Status);
    }

    [Fact]
    public void Reschedule_ToNull_MeansAsSoonAsPossible()
    {
        var publication = MakePublicationWithContent();
        var now = DateTime.UtcNow;
        publication.Queue(now.AddHours(1), now);

        publication.Reschedule(null, now);

        Assert.Null(publication.ScheduledAt);
        Assert.True(publication.IsDueToPublish(now));
    }

    [Fact]
    public void Reschedule_ToPastTime_Throws()
    {
        var publication = MakePublicationWithContent();
        var now = DateTime.UtcNow;
        publication.Queue(null, now);

        Assert.Throws<DomainException>(() => publication.Reschedule(now.AddMinutes(-1), now));
    }

    [Fact]
    public void Reschedule_WhileStillDraft_Throws()
    {
        var publication = MakePublicationWithContent();
        Assert.Throws<InvalidStateTransitionException>(() => publication.Reschedule(DateTime.UtcNow.AddHours(1), DateTime.UtcNow));
    }

    [Fact]
    public void Reschedule_AfterPublished_Throws()
    {
        var publication = MakePublicationWithContent();
        var now = DateTime.UtcNow;
        publication.Queue(null, now);
        publication.StartPublishing(now);
        publication.MarkPublished("ext-1", null, now);

        Assert.Throws<InvalidStateTransitionException>(() => publication.Reschedule(now.AddHours(1), now));
    }

    [Fact]
    public void Queue_TwiceFromDraft_SecondCallThrows()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.Queue(null, DateTime.UtcNow));
    }

    // ── StartPublishing (idempotency guard) ─────────────────────────────────

    [Fact]
    public void StartPublishing_FromQueued_MovesToPublishing()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        publication.StartPublishing(DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Publishing, publication.Status);
        Assert.NotNull(publication.StartedAt);
    }

    [Fact]
    public void StartPublishing_FromDraft_Throws()
    {
        var publication = MakePublicationWithContent();
        Assert.Throws<InvalidStateTransitionException>(() => publication.StartPublishing(DateTime.UtcNow));
    }

    [Fact]
    public void StartPublishing_Twice_SecondCallThrows_PreventingDuplicateExternalPost()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.StartPublishing(DateTime.UtcNow));
    }

    [Fact]
    public void StartPublishing_SetsLeaseUntil_ForTheReaperToFindLater()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        var now = DateTime.UtcNow;

        publication.StartPublishing(now);

        Assert.Equal(now.Add(SocialPublication.DefaultLeaseDuration), publication.LeaseUntil);
    }

    // ── ReleaseExpiredLease (Phase 1 audit F-8: recovering a crashed worker) ────────────────

    [Fact]
    public void ReleaseExpiredLease_PastDeadline_MovesToFailed_WithAmbiguousOutcome_NeverRetryable()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        var startedAt = DateTime.UtcNow;
        publication.StartPublishing(startedAt);

        publication.ReleaseExpiredLease(publication.LeaseUntil!.Value.AddSeconds(1));

        Assert.Equal(SocialPublicationStatus.Failed, publication.Status);
        Assert.Equal(SocialPublicationErrorCode.AmbiguousOutcome, publication.ErrorCode);
        Assert.Null(publication.LeaseUntil);
        Assert.Null(publication.NextRetryAt); // never auto-retried — could double-post a call that actually succeeded
    }

    [Fact]
    public void ReleaseExpiredLease_BeforeDeadline_IsANoOp()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        var now = DateTime.UtcNow;
        publication.StartPublishing(now);

        publication.ReleaseExpiredLease(now); // exactly at StartedAt, well before LeaseUntil

        Assert.Equal(SocialPublicationStatus.Publishing, publication.Status);
    }

    [Fact]
    public void ReleaseExpiredLease_OnANonPublishingRow_IsANoOp()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        publication.ReleaseExpiredLease(DateTime.UtcNow.AddHours(1));

        Assert.Equal(SocialPublicationStatus.Queued, publication.Status);
    }

    [Fact]
    public void MarkPublished_ClearsLeaseUntil()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        publication.MarkPublished("ext-1", null, DateTime.UtcNow);

        Assert.Null(publication.LeaseUntil);
    }

    // ── MarkPublished ────────────────────────────────────────────────────────

    [Fact]
    public void MarkPublished_FromPublishing_Succeeds()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        publication.MarkPublished("ext-post-1", "https://facebook.com/posts/1", DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Published, publication.Status);
        Assert.Equal("ext-post-1", publication.ExternalPostId);
        Assert.NotNull(publication.PublishedAt);
    }

    [Fact]
    public void MarkPublished_BlankExternalPostId_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        Assert.Throws<DomainException>(() => publication.MarkPublished("  ", null, DateTime.UtcNow));
    }

    [Fact]
    public void MarkPublished_FromQueued_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.MarkPublished("x", null, DateTime.UtcNow));
    }

    // ── MarkFailed: retryable vs non-retryable ──────────────────────────────

    [Fact]
    public void MarkFailed_RetryableError_MovesToRetrying_AndIncrementsRetryCount()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        publication.MarkFailed(SocialPublicationErrorCode.NetworkError, "network blip", DateTime.UtcNow, TimeSpan.FromMinutes(2));

        Assert.Equal(SocialPublicationStatus.Retrying, publication.Status);
        Assert.Equal(1, publication.RetryCount);
        Assert.NotNull(publication.NextRetryAt);
        Assert.Equal(SocialPublicationErrorCode.NetworkError, publication.ErrorCode);
    }

    [Fact]
    public void MarkFailed_NonRetryableError_MovesToFailed_WithoutIncrementingRetryCount()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        publication.MarkFailed(SocialPublicationErrorCode.InvalidCredentials, "bad token", DateTime.UtcNow, TimeSpan.FromMinutes(2));

        Assert.Equal(SocialPublicationStatus.Failed, publication.Status);
        Assert.Equal(0, publication.RetryCount);
        Assert.Null(publication.NextRetryAt);
    }

    [Fact]
    public void MarkFailed_RetryableError_ButRetryBudgetExhausted_MovesToFailed()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        // Exhaust the default retry budget by cycling Publishing -> Retrying -> (worker would
        // move it back to Publishing) repeatedly; here we just call the two methods directly.
        for (var i = 0; i < SocialPublication.DefaultMaxRetryCount; i++)
        {
            publication.StartPublishing(DateTime.UtcNow);
            publication.MarkFailed(SocialPublicationErrorCode.Timeout, "timeout", DateTime.UtcNow, TimeSpan.FromSeconds(1));
            Assert.Equal(SocialPublicationStatus.Retrying, publication.Status);
        }

        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkFailed(SocialPublicationErrorCode.Timeout, "timeout", DateTime.UtcNow, TimeSpan.FromSeconds(1));

        // MaxRetryCount reached — this last failure must land on Failed, not Retrying again.
        Assert.Equal(SocialPublicationStatus.Failed, publication.Status);
        Assert.Equal(SocialPublication.DefaultMaxRetryCount, publication.RetryCount);
    }

    [Fact]
    public void ErrorMessage_LongerThan500Characters_IsTruncated()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        publication.MarkFailed(SocialPublicationErrorCode.InvalidContent, new string('x', 2000), DateTime.UtcNow, TimeSpan.Zero);

        Assert.Equal(500, publication.ErrorMessage!.Length);
    }

    // ── RetryManually ────────────────────────────────────────────────────────

    [Fact]
    public void RetryManually_FromFailed_MovesToQueued()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkFailed(SocialPublicationErrorCode.InvalidCredentials, "bad", DateTime.UtcNow, TimeSpan.Zero);

        publication.RetryManually(DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Queued, publication.Status);
    }

    [Fact]
    public void RetryManually_FromNonFailedStatus_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.RetryManually(DateTime.UtcNow));
    }

    [Fact]
    public void RetryManually_AfterMaxRetryCountReached_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        for (var i = 0; i < SocialPublication.DefaultMaxRetryCount; i++)
        {
            publication.StartPublishing(DateTime.UtcNow);
            publication.MarkFailed(SocialPublicationErrorCode.Timeout, "timeout", DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkFailed(SocialPublicationErrorCode.Timeout, "timeout", DateTime.UtcNow, TimeSpan.FromSeconds(1));
        Assert.Equal(SocialPublicationStatus.Failed, publication.Status);

        Assert.Throws<InvalidStateTransitionException>(() => publication.RetryManually(DateTime.UtcNow));
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_FromDraft_Succeeds()
    {
        var publication = MakePublicationWithContent();
        publication.Cancel(DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Cancelled, publication.Status);
        Assert.NotNull(publication.CancelledAt);
    }

    [Fact]
    public void Cancel_FromQueued_Succeeds()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);

        publication.Cancel(DateTime.UtcNow);

        Assert.Equal(SocialPublicationStatus.Cancelled, publication.Status);
    }

    [Fact]
    public void Cancel_FromPublished_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkPublished("ext-1", null, DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.Cancel(DateTime.UtcNow));
    }

    [Fact]
    public void Cancel_FromPublishing_Throws()
    {
        var publication = MakePublicationWithContent();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.Cancel(DateTime.UtcNow));
    }

    [Fact]
    public void Cancel_Twice_SecondCallThrows()
    {
        var publication = MakePublicationWithContent();
        publication.Cancel(DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => publication.Cancel(DateTime.UtcNow));
    }

    // ── AttachContent ────────────────────────────────────────────────────────

    [Fact]
    public void AttachContent_Twice_Throws()
    {
        var publication = MakePublicationWithContent();
        var secondContent = SocialPostContent.Create(
            publication.Id, SocialPlatform.Facebook, "عنوان2", "نص2",
            "https://cdn.example.com/img2.jpg", "https://realestateworld.world/properties/p1", null, "ar");

        Assert.Throws<InvalidStateTransitionException>(() => publication.AttachContent(secondContent));
    }

    [Fact]
    public void AttachContent_ContentBelongingToDifferentPublication_Throws()
    {
        var publication = MakePublication();
        var mismatchedContent = SocialPostContent.Create(
            Guid.NewGuid(), SocialPlatform.Facebook, "عنوان", "نص",
            "https://cdn.example.com/img.jpg", "https://realestateworld.world/properties/p1", null, "ar");

        Assert.Throws<DomainException>(() => publication.AttachContent(mismatchedContent));
    }
}
