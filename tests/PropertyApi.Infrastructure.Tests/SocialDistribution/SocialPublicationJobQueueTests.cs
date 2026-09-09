using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Infrastructure.SocialDistribution;

namespace PropertyApi.Infrastructure.Tests.SocialDistribution;

/// <summary>Phase 6 spec §31.C — the Queue Port's production adapter.</summary>
public sealed class SocialPublicationJobQueueTests
{
    private static (SocialPublication publication, SocialAccount account) MakeQueuedPublication(SocialPlatform platform = SocialPlatform.Facebook)
    {
        var account = SocialAccount.Create(Guid.NewGuid(), platform, "Page", "ext-1", SocialAccountType.Page);
        account.Connect(null);

        var publication = SocialPublication.Create(Guid.NewGuid(), account.Id, Guid.NewGuid(), platform);
        var content = SocialPostContent.Create(
            publication.Id, platform, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://realestateworld.world/properties/p1", null, "ar");
        publication.AttachContent(content);
        publication.Queue(null, DateTime.UtcNow);

        return (publication, account);
    }

    [Fact]
    public async Task DequeueDueBatchAsync_MapsDuePublicationsToJobs_WithComputedIdempotencyKey()
    {
        var (publication, _) = MakeQueuedPublication();
        var repo = new Mock<ISocialPublicationRepository>();
        repo.Setup(x => x.GetDueToPublishAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([publication]);
        repo.Setup(x => x.GetDueForRetryAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var queue = new SocialPublicationJobQueue(repo.Object, NullLogger<SocialPublicationJobQueue>.Instance);
        var jobs = await queue.DequeueDueBatchAsync(25, DateTime.UtcNow, CancellationToken.None);

        var job = Assert.Single(jobs);
        Assert.Equal(publication.Id, job.JobId);
        Assert.Equal(publication.Id, job.PublicationId);
        Assert.Equal(SocialPlatform.Facebook, job.Platform);
        Assert.Equal($"social-publication:{publication.Id}:v1", job.IdempotencyKey);
        Assert.Equal(publication.RetryCount, job.Attempt);
    }

    [Fact]
    public async Task DequeueDueBatchAsync_CombinesDueAndRetryingWithoutDuplicates()
    {
        var (dueNow, _) = MakeQueuedPublication(SocialPlatform.Facebook);
        var (dueForRetry, _) = MakeQueuedPublication(SocialPlatform.Instagram);

        var repo = new Mock<ISocialPublicationRepository>();
        repo.Setup(x => x.GetDueToPublishAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([dueNow]);
        repo.Setup(x => x.GetDueForRetryAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([dueForRetry]);

        var queue = new SocialPublicationJobQueue(repo.Object, NullLogger<SocialPublicationJobQueue>.Instance);
        var jobs = await queue.DequeueDueBatchAsync(25, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(2, jobs.Count);
        Assert.Contains(jobs, j => j.PublicationId == dueNow.Id);
        Assert.Contains(jobs, j => j.PublicationId == dueForRetry.Id);
    }

    [Fact]
    public async Task EnqueueAsync_NeverThrows_IsAPureSignal()
    {
        var repo = new Mock<ISocialPublicationRepository>();
        var queue = new SocialPublicationJobQueue(repo.Object, NullLogger<SocialPublicationJobQueue>.Instance);

        await queue.EnqueueAsync(Guid.NewGuid(), CancellationToken.None);
        // No exception, no repository interaction required — see the Port's remarks on why this
        // production adapter's Enqueue is a documented no-op.
        repo.VerifyNoOtherCalls();
    }
}
