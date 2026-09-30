using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.EventHandlers;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>
/// Spec §9: "فشل توزيع المنشورات يجب ألا يجعل نشر العقار نفسه يفشل". These tests are the
/// cross-bounded-context contract's safety net — Listings' PublishPropertyCommandHandler trusts
/// this handler to never let an exception escape back through IPublisher.Publish.
/// </summary>
public sealed class PropertyPublishedDistributionHandlerTests
{
    [Fact]
    public async Task Handle_EngineSucceeds_CallsRunAsyncWithPropertyPublishedTrigger()
    {
        var engine = new Mock<IDistributionEngine>();
        var propertyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        engine.Setup(x => x.RunAsync(propertyId, DistributionRunTriggerType.PropertyPublished, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DistributionRunDto(Guid.NewGuid(), propertyId, DistributionRunTriggerType.PropertyPublished,
                DistributionRunStatus.Completed, DateTime.UtcNow, DateTime.UtcNow, 1, 1, 0, null, DateTime.UtcNow));

        var handler = new PropertyPublishedDistributionHandler(engine.Object, NullLogger<PropertyPublishedDistributionHandler>.Instance);

        await handler.Handle(new PropertyPublishedEvent(propertyId, DateTime.UtcNow, userId), CancellationToken.None);

        engine.Verify(x => x.RunAsync(propertyId, DistributionRunTriggerType.PropertyPublished, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EngineThrowsConflict_ExceptionIsSwallowed_NeverPropagates()
    {
        var engine = new Mock<IDistributionEngine>();
        engine.Setup(x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<DistributionRunTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("العقار لم يعد متاحًا."));

        var handler = new PropertyPublishedDistributionHandler(engine.Object, NullLogger<PropertyPublishedDistributionHandler>.Instance);

        // Must complete without throwing — this is the entire point of the handler.
        await handler.Handle(new PropertyPublishedEvent(Guid.NewGuid(), DateTime.UtcNow, null), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_EngineThrowsUnexpectedException_ExceptionIsSwallowed_NeverPropagates()
    {
        var engine = new Mock<IDistributionEngine>();
        engine.Setup(x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<DistributionRunTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var handler = new PropertyPublishedDistributionHandler(engine.Object, NullLogger<PropertyPublishedDistributionHandler>.Instance);

        await handler.Handle(new PropertyPublishedEvent(Guid.NewGuid(), DateTime.UtcNow, null), CancellationToken.None);
    }
}
