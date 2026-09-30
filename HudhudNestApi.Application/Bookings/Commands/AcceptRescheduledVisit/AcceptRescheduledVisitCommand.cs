using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Bookings.Commands.AcceptRescheduledVisit;

/// <summary>Requester-side: accepts the owner's counter-proposed date/time.</summary>
public sealed record AcceptRescheduledVisitCommand(
    Guid VisitId,
    Guid RequesterId) : IRequest<bool>;

public sealed class AcceptRescheduledVisitCommandHandler
    : IRequestHandler<AcceptRescheduledVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;
    private readonly ILogger<AcceptRescheduledVisitCommandHandler> _logger;

    public AcceptRescheduledVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<AcceptRescheduledVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
    }

    public async Task<bool> Handle(AcceptRescheduledVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        if (visit.RequesterId != request.RequesterId)
        {
            throw new ForbiddenException("Only the requester can accept the rescheduled visit.");
        }

        visit.AcceptReschedule();
        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: property.OwnerId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitRescheduleAccepted,
                detail: $"{visit.VisitorName} — الموعد المؤكَّد: {visit.ProposedAt:dd/MM/yyyy HH:mm}.",
                relatedEntityId: visit.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-reschedule-accepted notification. VisitId={VisitId}, OwnerId={OwnerId}",
                visit.Id,
                property.OwnerId);
        }

        return true;
    }
}
