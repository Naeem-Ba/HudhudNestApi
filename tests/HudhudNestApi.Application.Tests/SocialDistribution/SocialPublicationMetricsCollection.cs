namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>
/// xUnit runs test classes in parallel. The publish-attempt counter is a process-wide static Meter,
/// so SocialPublicationMetricsTests also saw measurements from handler tests running at the same
/// time ("Assert.Single() Failure: The collection contained 2 items"). Classes in one collection
/// run serially, which makes the assertion deterministic.
/// </summary>
public static class SocialPublicationMetricsCollection
{
    public const string Name = "SocialPublicationMetrics";
}

[CollectionDefinition(SocialPublicationMetricsCollection.Name)]
public sealed class SocialPublicationMetricsCollectionDefinition;
