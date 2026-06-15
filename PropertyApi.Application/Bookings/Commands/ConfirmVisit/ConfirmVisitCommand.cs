using MediatR;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Bookings.Commands.ConfirmVisit;

public sealed record ConfirmVisitCommand(
    Guid VisitId,
    Guid OwnerId,
    string? OwnerNote = null) : IRequest<bool>;

public sealed class ConfirmVisitCommandHandler : IRequestHandler<ConfirmVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;

    public ConfirmVisitCommandHandler(
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

    public async Task<bool> Handle(ConfirmVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        if (property.OwnerId != request.OwnerId)
        {
            throw new ForbiddenException("Only the property owner can confirm this visit.");
        }

        visit.Confirm(request.OwnerNote);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyPropertyUpdateAsync(
            recipientId: visit.RequesterId,
            propertyId: property.Id,
            propertyTitle: property.Title,
            type: NotificationType.VisitConfirmed,
            detail: $"Your visit request scheduled for {visit.ProposedAt:dd/MM/yyyy HH:mm} was confirmed.",
            ct: ct);

        return true;
    }
}
