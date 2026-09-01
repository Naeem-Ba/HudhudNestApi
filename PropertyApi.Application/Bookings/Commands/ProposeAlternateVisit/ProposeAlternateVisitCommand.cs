using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Bookings.Commands.ProposeAlternateVisit;

/// <summary>
/// Owner-side: instead of confirming/declining the requester's proposed time as-is,
/// counter-propose a different date/time. Mirrors ConfirmVisitCommand/DeclineVisitCommand.
/// </summary>
public sealed record ProposeAlternateVisitCommand(
    Guid VisitId,
    Guid OwnerId,
    DateTime ProposedAt,
    string? OwnerNote = null) : IRequest<bool>;

public sealed class ProposeAlternateVisitCommandHandler
    : IRequestHandler<ProposeAlternateVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;
    private readonly ILogger<ProposeAlternateVisitCommandHandler> _logger;

    public ProposeAlternateVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<ProposeAlternateVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
    }

    public async Task<bool> Handle(ProposeAlternateVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        if (property.OwnerId != request.OwnerId)
        {
            throw new ForbiddenException("Only the property owner can propose an alternate time for this visit.");
        }

        visit.ProposeAlternate(request.ProposedAt, request.OwnerNote);
        await _uow.SaveChangesAsync(ct);

        try
        {
            var noteSuffix = string.IsNullOrWhiteSpace(visit.OwnerNote)
                ? string.Empty
                : $" — ملاحظة المالك: {visit.OwnerNote}";

            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: visit.RequesterId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitRescheduleProposed,
                detail: $"الموعد الجديد المقترح: {visit.ProposedAt:dd/MM/yyyy HH:mm}.{noteSuffix}",
                relatedEntityId: visit.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-reschedule-proposed notification. VisitId={VisitId}, RequesterId={RequesterId}",
                visit.Id,
                visit.RequesterId);
        }

        return true;
    }
}
