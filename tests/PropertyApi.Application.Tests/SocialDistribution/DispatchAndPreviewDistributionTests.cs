using MediatR;
using Moq;
using PropertyApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Queries.PreviewPropertyDistribution;
using PropertyApi.Application.SocialDistribution.Services;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.Tests.SocialDistribution;

public sealed class DispatchAndPreviewDistributionTests
{
    [Fact]
    public async Task DispatchPropertyDistributionCommandHandler_DelegatesToEngine_WithManualTrigger()
    {
        var engine = new Mock<IDistributionEngine>();
        var propertyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expected = new DistributionRunDto(Guid.NewGuid(), propertyId, DistributionRunTriggerType.Manual,
            DistributionRunStatus.Completed, DateTime.UtcNow, DateTime.UtcNow, 1, 1, 0, null, DateTime.UtcNow);

        engine.Setup(x => x.RunAsync(propertyId, DistributionRunTriggerType.Manual, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new DispatchPropertyDistributionCommandHandler(engine.Object);
        var result = await handler.Handle(new DispatchPropertyDistributionCommand(propertyId, userId), CancellationToken.None);

        Assert.Equal(expected, result);
        engine.Verify(x => x.RunAsync(propertyId, DistributionRunTriggerType.Manual, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviewPropertyDistributionQueryHandler_DelegatesToEnginePreview()
    {
        var engine = new Mock<IDistributionEngine>();
        var propertyId = Guid.NewGuid();
        var expected = new DistributionPreviewDto(propertyId, 0, []);

        engine.Setup(x => x.PreviewAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var handler = new PreviewPropertyDistributionQueryHandler(engine.Object);
        var result = await handler.Handle(new PreviewPropertyDistributionQuery(propertyId), CancellationToken.None);

        Assert.Equal(expected, result);
    }
}
