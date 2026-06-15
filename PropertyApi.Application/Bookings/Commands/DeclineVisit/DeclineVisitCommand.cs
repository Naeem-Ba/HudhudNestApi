using MediatR;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Bookings.Commands.DeclineVisit;

public sealed record DeclineVisitCommand(
    Guid VisitId,
    Guid OwnerId,
    string? Reason = null) : IRequest<bool>;

public sealed class DeclineVisitCommandHandler : IRequestHandler<DeclineVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;

    public DeclineVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
    }

    public async Task<bool> Handle(DeclineVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        if (property.OwnerId != request.OwnerId)
        {
            throw new ForbiddenException("Only the property owner can decline this visit.");
        }

        visit.Decline(request.Reason);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyPropertyUpdateAsync(
            recipientId: visit.RequesterId,
            propertyId: property.Id,
            propertyTitle: property.Title,
            type: NotificationType.VisitDeclined,
            detail: request.Reason ?? "Your visit request was declined.",
            ct: ct);

        return true;
    }
}
