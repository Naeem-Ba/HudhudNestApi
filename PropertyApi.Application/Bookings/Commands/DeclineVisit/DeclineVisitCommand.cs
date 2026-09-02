using MediatR;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<DeclineVisitCommandHandler> _logger;

    public DeclineVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<DeclineVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
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

        // ✅ إصلاح: نفس نمط try/catch الموجود بـ RequestVisitCommandHandler —
        // الزيارة رُفضت وحُفظت أعلاه بالفعل، فلا يجوز لفشل إرسال الإشعار أن
        // يُسقط طلب الرفض بأكمله بـ 500.
        try
        {
            var noteSuffix = string.IsNullOrWhiteSpace(visit.OwnerNote)
                ? string.Empty
                : $" — سبب الرفض: {visit.OwnerNote}";

            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: visit.RequesterId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitDeclined,
                detail: $"طلب الزيارة بتاريخ {visit.ProposedAt:dd/MM/yyyy HH:mm}.{noteSuffix}",
                relatedEntityId: visit.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-declined notification. VisitId={VisitId}, RequesterId={RequesterId}",
                visit.Id,
                visit.RequesterId);
        }

        return true;
    }
}

