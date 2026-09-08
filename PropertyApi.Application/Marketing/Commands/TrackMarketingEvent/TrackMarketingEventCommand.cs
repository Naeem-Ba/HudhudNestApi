using MediatR;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Marketing.Commands.TrackMarketingEvent;

public sealed record TrackMarketingEventCommand(
    MarketingEventType EventType,
    string Source,
    string? Campaign,
    string? SessionId,
    string? Path,
    Guid? LeadId,
    Guid? OfferId) : IRequest<Guid>;
