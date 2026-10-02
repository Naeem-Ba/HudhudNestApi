using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Observability;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>
/// Every finished publish attempt is counted by platform and outcome — the signal an alert needs
/// to notice "the Telegram token was rejected" or "publishing keeps failing" without anyone
/// reading logs. Labels are the metric-cardinality policy's bounded kind only: a platform enum,
/// an outcome, and the error-code enum name — never an account, property or publication id.
/// </summary>
public sealed class SocialPublicationMetricsTests
{
    private const string InstrumentName = "hudhudnest.social.publication.attempts";

    private sealed class Capture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(long Value, Dictionary<string, object?> Tags)> _measurements = [];

        public Capture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationTelemetry.MeterName && instrument.Name == InstrumentName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                var copy = new Dictionary<string, object?>();
                foreach (var tag in tags)
                    copy[tag.Key] = tag.Value;
                lock (_measurements) _measurements.Add((value, copy));
            });
            _listener.Start();
        }

        public (long Value, Dictionary<string, object?> Tags) Single()
        {
            lock (_measurements) return Assert.Single(_measurements);
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class FakePublisher(SocialPlatform platform, SocialPublishResult result) : ISocialPublisher
    {
        public SocialPlatform Platform { get; } = platform;

        public SocialPublisherCapabilities GetCapabilities() => new(
            SupportsText: true, SupportsImages: true, SupportsVideo: false, SupportsStories: false,
            SupportsHashtags: true, SupportsScheduling: false, SupportsUpdate: false, SupportsDelete: false);

        public SocialContentValidationResult ValidateContent(SocialPublishRequest request) => SocialContentValidationResult.Valid;

        public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default) => Task.FromResult(result);

        public Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default) =>
            Task.FromResult(result);

        public Task<SocialPublishResult> CommentAsync(SocialPublishRequest request, string externalPostId, string commentBody, CancellationToken ct = default) =>
            Task.FromResult(result);

        public Task<SocialPublishResult> DeleteAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private static async Task RunAsync(SocialPublishResult result, bool propertyStillPublic = true)
    {
        var account = SocialAccount.Create(Guid.NewGuid(), SocialPlatform.Telegram, "Channel", "@hudhudnest", SocialAccountType.Channel);
        account.Connect(null);
        var publication = SocialPublication.Create(Guid.NewGuid(), account.Id, Guid.NewGuid(), SocialPlatform.Telegram);
        publication.AttachContent(SocialPostContent.Create(
            publication.Id, SocialPlatform.Telegram, "عنوان", "نص", "https://cdn.example.com/img.jpg", "https://hudhudnest.com/properties/p1", null, "ar"));
        publication.Queue(null, DateTime.UtcNow);

        var publications = new Mock<ISocialPublicationRepository>();
        publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        var accounts = new Mock<ISocialAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(propertyStillPublic);
        var registry = new Mock<ISocialPublisherRegistry>();
        registry.Setup(x => x.TryGetPublisher(SocialPlatform.Telegram)).Returns(new FakePublisher(SocialPlatform.Telegram, result));

        var handler = new PublishSocialPublicationCommandHandler(
            publications.Object, new Mock<ISocialPublicationStatusHistoryRepository>().Object, accounts.Object, properties.Object,
            registry.Object, new Mock<ISocialMediaAssetGenerator>().Object, new Mock<IUnitOfWork>().Object,
            NullLogger<PublishSocialPublicationCommandHandler>.Instance);

        await handler.Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);
    }

    [Fact]
    public async Task ASuccessfulPublish_IsCounted_AsPublished_WithoutAFailureCategory()
    {
        using var capture = new Capture();

        await RunAsync(SocialPublishResult.Success("42"));

        var (value, tags) = capture.Single();
        Assert.Equal(1, value);
        Assert.Equal("Telegram", tags["platform"]);
        Assert.Equal("published", tags["outcome"]);
        Assert.False(tags.ContainsKey("failure_reason_category"));
    }

    [Fact]
    public async Task ARetryableFailure_IsCounted_AsRetrying_WithItsErrorCodeCategory()
    {
        using var capture = new Capture();

        await RunAsync(SocialPublishResult.Failure(SocialPublicationErrorCode.RateLimited, "slow down"));

        var (_, tags) = capture.Single();
        Assert.Equal("retrying", tags["outcome"]);
        Assert.Equal("RateLimited", tags["failure_reason_category"]);
    }

    [Fact]
    public async Task ARejectedCredential_IsCounted_AsFailed_WithTheInvalidCredentialsCategory()
    {
        using var capture = new Capture();

        await RunAsync(SocialPublishResult.Failure(SocialPublicationErrorCode.InvalidCredentials, "token rejected"));

        var (_, tags) = capture.Single();
        Assert.Equal("failed", tags["outcome"]);
        Assert.Equal("InvalidCredentials", tags["failure_reason_category"]);
    }

    [Fact]
    public async Task APublicationThatNeverReachedThePlatform_IsStillCounted_AsFailed()
    {
        using var capture = new Capture();

        await RunAsync(SocialPublishResult.Success("unused"), propertyStillPublic: false);

        var (_, tags) = capture.Single();
        Assert.Equal("failed", tags["outcome"]);
        Assert.Equal("PropertyNotPublic", tags["failure_reason_category"]);
    }

    [Fact]
    public async Task NoMeasurement_CarriesAnIdentifierLabel()
    {
        using var capture = new Capture();

        await RunAsync(SocialPublishResult.Success("42"));

        var (_, tags) = capture.Single();
        Assert.Equal(new[] { "outcome", "platform" }, tags.Keys.OrderBy(key => key).ToArray());
    }
}
