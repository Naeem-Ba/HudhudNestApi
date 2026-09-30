using MediatR;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.TrackMarketingEvent;

public sealed record TrackMarketingEventCommand(
    MarketingEventType EventType,
    string Source,
    string? Campaign,
    string? SessionId,
    string? Path,
    Guid? LeadId,
    Guid? OfferId) : IRequest<Guid>;
