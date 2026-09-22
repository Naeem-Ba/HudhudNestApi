using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class DistributionRunTests
{
    [Fact]
    public void Create_ValidInput_StartsPending()
    {
        var run = DistributionRun.Create(Guid.NewGuid(), DistributionRunTriggerType.PropertyPublished, null);
        Assert.Equal(DistributionRunStatus.Pending, run.Status);
    }

    [Fact]
    public void Create_EmptyPropertyId_Throws() =>
        Assert.Throws<DomainException>(() => DistributionRun.Create(Guid.Empty, DistributionRunTriggerType.Manual, null));

    [Fact]
    public void MarkEvaluating_FromPending_Succeeds()
    {
        var run = DistributionRun.Create(Guid.NewGuid(), DistributionRunTriggerType.Manual, Guid.NewGuid());
        run.MarkEvaluating(DateTime.UtcNow);

        Assert.Equal(DistributionRunStatus.Evaluating, run.Status);
        Assert.NotNull(run.StartedAt);
    }

    [Fact]
    public void MarkEvaluating_Twice_Throws()
    {
        var run = DistributionRun.Create(Guid.NewGuid(), DistributionRunTriggerType.Manual, null);
        run.MarkEvaluating(DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => run.MarkEvaluating(DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0, 0, DistributionRunStatus.Completed)]        // no rules matched — a valid, non-error outcome
    [InlineData(3, 0, DistributionRunStatus.Completed)]        // every matched account got a publication
    [InlineData(2, 1, DistributionRunStatus.PartiallyCompleted)]
    [InlineData(0, 2, DistributionRunStatus.Failed)]           // matched accounts existed, all were ineligible
    public void Complete_DerivesStatusFromCounters(int createdCount, int skippedCount, DistributionRunStatus expected)
    {
        var run = DistributionRun.Create(Guid.NewGuid(), DistributionRunTriggerType.PropertyPublished, null);
        run.MarkEvaluating(DateTime.UtcNow);

        run.Complete(matchedRuleCount: createdCount + skippedCount, createdCount, skippedCount, DateTime.UtcNow, null);

        Assert.Equal(expected, run.Status);
        Assert.Equal(createdCount, run.PublicationsCreatedCount);
        Assert.Equal(skippedCount, run.SkippedCount);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public void Complete_WithoutMarkEvaluatingFirst_Throws()
    {
        var run = DistributionRun.Create(Guid.NewGuid(), DistributionRunTriggerType.Manual, null);
        Assert.Throws<InvalidStateTransitionException>(() => run.Complete(0, 0, 0, DateTime.UtcNow, null));
    }

    [Fact]
    public void MarkFailedToStart_FromPending_Succeeds()
    {
        var run = DistributionRun.Create(Guid.NewGuid(), DistributionRunTriggerType.Manual, null);
        run.MarkFailedToStart("PropertyNotPublic", DateTime.UtcNow);

        Assert.Equal(DistributionRunStatus.Failed, run.Status);
        Assert.Equal("PropertyNotPublic", run.ResultReason);
    }
}
