using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Bookings.Services;

public sealed class ListingVisitCleanup : IListingVisitCleanup
{
    private readonly IVisitRepository _visits;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<ListingVisitCleanup> _logger;

    public ListingVisitCleanup(
        IVisitRepository visits,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<ListingVisitCleanup> logger)
    {
        _visits = visits;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> CancelOpenVisitsAsync(Guid propertyId, string propertyTitle, CancellationToken ct = default)
    {
        var open = await _visits.GetOpenByPropertyIdAsync(propertyId, ct);
        var cancelled = open.Where(visit => visit.CancelBecauseListingRemoved()).ToList();

        if (cancelled.Count == 0)
            return 0;

        await _uow.SaveChangesAsync(ct);

        // The visits are closed and saved; telling the visitor is best-effort, one failure must not stop
        // the others or fail the delete that already happened.
        foreach (var visit in cancelled)
        {
            try
            {
                await _notifications.NotifyPropertyUpdateAsync(
                    recipientId: visit.RequesterId,
                    propertyId: propertyId,
                    propertyTitle: propertyTitle,
                    type: NotificationType.VisitCancelledByHost,
                    detail: NotificationText.Of(NotificationKeys.VisitCancelledListingRemovedDetail, ("at", visit.ProposedAt)),
                    relatedEntityId: visit.Id,
                    ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Failed to tell a visitor that their visit was cancelled with the listing. VisitId={VisitId}",
                    visit.Id);
            }
        }

        _logger.LogInformation(
            "Cancelled {Count} open visit(s) of the deleted listing {PropertyId}.", cancelled.Count, propertyId);

        return cancelled.Count;
    }
}
