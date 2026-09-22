using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Commands.TrackPropertyShareEvent;

/// <summary>
/// Validates that the target listing is still publicly visible — never trusts that a client
/// asking to log a share event is telling the truth about that — then records the event.
/// See PropertyShareEvent's doc comment for exactly what is (and is not) stored.
/// </summary>
public sealed class TrackPropertyShareEventCommandHandler
    : IRequestHandler<TrackPropertyShareEventCommand, Guid>
{
    private readonly IPropertyRepository _properties;
    private readonly IPropertyShareEventRepository _shareEvents;
    private readonly IUnitOfWork _unitOfWork;

    public TrackPropertyShareEventCommandHandler(
        IPropertyRepository properties,
        IPropertyShareEventRepository shareEvents,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _shareEvents = shareEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(TrackPropertyShareEventCommand request, CancellationToken cancellationToken)
    {
        var isVisible = await _properties.IsPubliclyVisibleAsync(request.PropertyId, cancellationToken);
        if (!isVisible)
            throw new NotFoundException("Property was not found.");

        var shareEvent = PropertyShareEvent.Create(
            request.PropertyId,
            request.Platform,
            request.UserId,
            request.UtmSource,
            request.UtmMedium,
            request.UtmCampaign,
            request.UtmContent);

        _shareEvents.Add(shareEvent);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return shareEvent.Id;
    }
}
