using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Bookings.Commands.ConfirmVisit;

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
    private readonly ILogger<ConfirmVisitCommandHandler> _logger;

    public ConfirmVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<ConfirmVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
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

        // ✅ إصلاح: نفس نمط try/catch الموجود بـ RequestVisitCommandHandler —
        // الزيارة تأكدت وحُفظت أعلاه بالفعل، فلا يجوز لفشل إرسال الإشعار
        // (SignalR مثلاً) أن يُسقط طلب التأكيد بأكمله بـ 500.
        try
        {
            var noteSuffix = string.IsNullOrWhiteSpace(visit.OwnerNote)
                ? string.Empty
                : $" — ملاحظة المالك: {visit.OwnerNote}";

            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: visit.RequesterId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitConfirmed,
                detail: $"موعد الزيارة بتاريخ {visit.ProposedAt:dd/MM/yyyy HH:mm}.{noteSuffix}",
                relatedEntityId: visit.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-confirmed notification. VisitId={VisitId}, RequesterId={RequesterId}",
                visit.Id,
                visit.RequesterId);
        }

        return true;
    }
}

