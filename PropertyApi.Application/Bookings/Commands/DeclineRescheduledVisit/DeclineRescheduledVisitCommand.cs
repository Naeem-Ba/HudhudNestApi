using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Bookings.Commands.DeclineRescheduledVisit;

/// <summary>Requester-side: declines the owner's counter-proposed date/time.</summary>
public sealed record DeclineRescheduledVisitCommand(
    Guid VisitId,
    Guid RequesterId) : IRequest<bool>;

public sealed class DeclineRescheduledVisitCommandHandler
    : IRequestHandler<DeclineRescheduledVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;
    private readonly ILogger<DeclineRescheduledVisitCommandHandler> _logger;

    public DeclineRescheduledVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<DeclineRescheduledVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
    }

    public async Task<bool> Handle(DeclineRescheduledVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        if (visit.RequesterId != request.RequesterId)
        {
            throw new ForbiddenException("Only the requester can decline the rescheduled visit.");
        }

        visit.DeclineReschedule();
        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: property.OwnerId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitRescheduleDeclined,
                detail: $"{visit.VisitorName} — الموعد المرفوض: {visit.ProposedAt:dd/MM/yyyy HH:mm}.",
                relatedEntityId: visit.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-reschedule-declined notification. VisitId={VisitId}, OwnerId={OwnerId}",
                visit.Id,
                property.OwnerId);
        }

        return true;
    }
}
