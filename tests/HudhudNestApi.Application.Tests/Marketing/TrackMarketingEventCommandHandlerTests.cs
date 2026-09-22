using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Commands.TrackMarketingEvent;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Tests.Marketing;

public sealed class TrackMarketingEventCommandHandlerTests
{
    [Fact]
    public async Task Handle_PersistsEventAndSaves()
    {
        var events = new Mock<IMarketingEventRepository>();
        MarketingEvent? added = null;
        events.Setup(x => x.Add(It.IsAny<MarketingEvent>())).Callback<MarketingEvent>(e => added = e);

        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackMarketingEventCommandHandler(events.Object, uow.Object);

        var result = await handler.Handle(
            new TrackMarketingEventCommand(
                MarketingEventType.CtaClick,
                "landing-page",
                Campaign: null,
                SessionId: "sess-1",
                Path: "/landing",
                LeadId: null,
                OfferId: null),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(result, added!.Id);
        Assert.Equal(MarketingEventType.CtaClick, added.EventType);
        events.Verify(x => x.Add(It.IsAny<MarketingEvent>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
