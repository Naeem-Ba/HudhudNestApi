using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Commands.SubmitLead;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Tests.Marketing;

public sealed class SubmitLeadCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithoutOfferId_CreatesLeadWithNoOfferLink()
    {
        var leads = new Mock<ILeadRepository>();
        Lead? added = null;
        leads.Setup(x => x.Add(It.IsAny<Lead>())).Callback<Lead>(l => added = l);

        var offers = new Mock<IOfferRepository>();
        var handler = new SubmitLeadCommandHandler(leads.Object, offers.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new SubmitLeadCommand(
                "أحمد", "0933123456", "دمشق", "agency", "landing-page",
                Notes: null, Campaign: null, OfferId: null, IpAddress: "1.2.3.4"),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Null(added!.OfferId);
        Assert.False(result.OfferRequested);
        Assert.False(result.OfferApplied);
        offers.Verify(x => x.TryReserveRedemptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithOfferIdAndAvailableSlot_LinksLeadToOffer()
    {
        var offerId = Guid.NewGuid();

        var leads = new Mock<ILeadRepository>();
        Lead? added = null;
        leads.Setup(x => x.Add(It.IsAny<Lead>())).Callback<Lead>(l => added = l);

        var offers = new Mock<IOfferRepository>();
        offers
            .Setup(x => x.TryReserveRedemptionAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new SubmitLeadCommandHandler(leads.Object, offers.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new SubmitLeadCommand(
                "أحمد", "0933123456", "دمشق", "agency", "landing-page",
                Notes: null, Campaign: null, OfferId: offerId, IpAddress: null),
            CancellationToken.None);

        Assert.Equal(offerId, added!.OfferId);
        Assert.True(result.OfferRequested);
        Assert.True(result.OfferApplied);
    }

    /// <summary>
    /// The core "first 100 agencies" guarantee: a submission arriving after the offer is
    /// already full must still create the Lead (the visitor is never turned away), but must
    /// NOT be silently counted as having claimed the offer.
    /// </summary>
    [Fact]
    public async Task Handle_WithOfferIdButNoSlotAvailable_StillCreatesLeadWithoutOfferLink()
    {
        var offerId = Guid.NewGuid();

        var leads = new Mock<ILeadRepository>();
        Lead? added = null;
        leads.Setup(x => x.Add(It.IsAny<Lead>())).Callback<Lead>(l => added = l);

        var offers = new Mock<IOfferRepository>();
        offers
            .Setup(x => x.TryReserveRedemptionAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var uow = new Mock<IUnitOfWork>();
        var handler = new SubmitLeadCommandHandler(leads.Object, offers.Object, uow.Object);

        var result = await handler.Handle(
            new SubmitLeadCommand(
                "أحمد", "0933123456", "دمشق", "agency", "landing-page",
                Notes: null, Campaign: null, OfferId: offerId, IpAddress: null),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Null(added!.OfferId);
        Assert.True(result.OfferRequested);
        Assert.False(result.OfferApplied);
        leads.Verify(x => x.Add(It.IsAny<Lead>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
