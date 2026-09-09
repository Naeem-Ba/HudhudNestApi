using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Commands.TrackPropertyAttributionEvent;

/// <summary>
/// Validates that the target listing is still publicly visible — never trusts that a client
/// asking to log a view/contact event is telling the truth about that — then records the event.
/// Mirrors TrackPropertyShareEventCommandHandler's shape exactly (same visibility check, same
/// "throw NotFoundException, don't leak whether the property ever existed" behavior).
/// </summary>
public sealed class TrackPropertyAttributionEventCommandHandler
    : IRequestHandler<TrackPropertyAttributionEventCommand, Guid>
{
    private readonly IPropertyRepository _properties;
    private readonly IPropertyAttributionEventRepository _attributionEvents;
    private readonly IUnitOfWork _unitOfWork;

    public TrackPropertyAttributionEventCommandHandler(
        IPropertyRepository properties,
        IPropertyAttributionEventRepository attributionEvents,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _attributionEvents = attributionEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(TrackPropertyAttributionEventCommand request, CancellationToken cancellationToken)
    {
        var isVisible = await _properties.IsPubliclyVisibleAsync(request.PropertyId, cancellationToken);
        if (!isVisible)
            throw new NotFoundException("Property was not found.");

        var attributionEvent = PropertyAttributionEvent.Create(
            request.PropertyId,
            request.EventType,
            request.UserId,
            request.UtmSource,
            request.UtmMedium,
            request.UtmCampaign,
            request.UtmContent);

        _attributionEvents.Add(attributionEvent);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return attributionEvent.Id;
    }
}
