using MediatR;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Bookings.Commands.CancelVisit;

public sealed record CancelVisitCommand(Guid VisitId, Guid RequesterId) : IRequest<bool>;

public sealed class CancelVisitCommandHandler : IRequestHandler<CancelVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;

    public CancelVisitCommandHandler(
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

    public async Task<bool> Handle(CancelVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        visit.Cancel(request.RequesterId);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyPropertyUpdateAsync(
            recipientId: property.OwnerId,
            propertyId: property.Id,
            propertyTitle: property.Title,
            type: NotificationType.VisitCancelled,
            detail: $"{visit.VisitorName} cancelled the visit request scheduled for {visit.ProposedAt:dd/MM/yyyy HH:mm}.",
            ct: ct);

        return true;
    }
}

