using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Bookings.Commands.CancelVisit;

public sealed record CancelVisitCommand(Guid VisitId, Guid RequesterId) : IRequest<bool>;

public sealed class CancelVisitCommandHandler : IRequestHandler<CancelVisitCommand, bool>
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IPropertyReadRepository _properties;
    private readonly ILogger<CancelVisitCommandHandler> _logger;

    public CancelVisitCommandHandler(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        IPropertyReadRepository properties,
        ILogger<CancelVisitCommandHandler> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _properties = properties;
        _logger = logger;
    }

    public async Task<bool> Handle(CancelVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(request.VisitId, ct)
            ?? throw new NotFoundException($"Visit {request.VisitId} was not found.");

        var property = await _properties.GetByIdAsync(visit.PropertyId, ct)
            ?? throw new NotFoundException("Property was not found.");

        visit.Cancel(request.RequesterId);
        await _uow.SaveChangesAsync(ct);

        // ✅ إصلاح: نفس نمط try/catch الموجود بـ RequestVisitCommandHandler —
        // الزيارة أُلغيت وحُفظت أعلاه بالفعل، فلا يجوز لفشل إرسال الإشعار أن
        // يُسقط طلب الإلغاء بأكمله بـ 500.
        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: property.OwnerId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.VisitCancelled,
                detail: $"{visit.VisitorName} — الموعد الملغى: {visit.ProposedAt:dd/MM/yyyy HH:mm}.",
                relatedEntityId: visit.Id,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send visit-cancelled notification. VisitId={VisitId}, OwnerId={OwnerId}",
                visit.Id,
                property.OwnerId);
        }

        return true;
    }
}

