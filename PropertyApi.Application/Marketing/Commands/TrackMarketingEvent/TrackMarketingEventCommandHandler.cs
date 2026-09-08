using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Commands.TrackMarketingEvent;

public sealed class TrackMarketingEventCommandHandler : IRequestHandler<TrackMarketingEventCommand, Guid>
{
    private readonly IMarketingEventRepository _events;
    private readonly IUnitOfWork _uow;

    public TrackMarketingEventCommandHandler(IMarketingEventRepository events, IUnitOfWork uow)
    {
        _events = events;
        _uow = uow;
    }

    public async Task<Guid> Handle(TrackMarketingEventCommand request, CancellationToken cancellationToken)
    {
        var marketingEvent = MarketingEvent.Create(
            request.EventType,
            request.Source,
            request.Campaign,
            request.SessionId,
            request.Path,
            request.LeadId,
            request.OfferId);

        _events.Add(marketingEvent);
        await _uow.SaveChangesAsync(cancellationToken);

        return marketingEvent.Id;
    }
}
