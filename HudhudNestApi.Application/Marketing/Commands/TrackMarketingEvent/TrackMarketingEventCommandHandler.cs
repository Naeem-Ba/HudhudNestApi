using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Commands.TrackMarketingEvent;

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
