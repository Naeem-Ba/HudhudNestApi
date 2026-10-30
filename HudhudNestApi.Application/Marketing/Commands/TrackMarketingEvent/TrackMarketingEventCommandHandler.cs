using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Commands.TrackMarketingEvent;

public sealed class TrackMarketingEventCommandHandler : IRequestHandler<TrackMarketingEventCommand, Guid>
{
    private readonly IMarketingEventRepository _events;
    private readonly IUnitOfWork _uow;
    private readonly ILeadRepository _leads;
    private readonly IOfferRepository _offers;

    public TrackMarketingEventCommandHandler(
        IMarketingEventRepository events,
        IUnitOfWork uow,
        ILeadRepository leads,
        IOfferRepository offers)
    {
        _events = events;
        _uow = uow;
        _leads = leads;
        _offers = offers;
    }

    public async Task<Guid> Handle(TrackMarketingEventCommand request, CancellationToken cancellationToken)
    {
        // Launch audit A2-003: this endpoint is public and the offer id comes from a campaign link,
        // so an old link to a deleted offer (or a made-up lead id) reached the foreign key and came
        // back as a 500. The event itself is still worth recording; only the dangling link is dropped.
        var leadId = request.LeadId is { } lead && await _leads.GetByIdAsync(lead, cancellationToken) is not null
            ? request.LeadId
            : null;
        var offerId = request.OfferId is { } offer && await _offers.GetByIdAsync(offer, cancellationToken) is not null
            ? request.OfferId
            : null;

        var marketingEvent = MarketingEvent.Create(
            request.EventType,
            request.Source,
            request.Campaign,
            request.SessionId,
            request.Path,
            leadId,
            offerId);

        _events.Add(marketingEvent);
        await _uow.SaveChangesAsync(cancellationToken);

        return marketingEvent.Id;
    }
}
