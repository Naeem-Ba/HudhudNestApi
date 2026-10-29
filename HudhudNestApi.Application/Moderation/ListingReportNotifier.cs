using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Moderation;

/// <summary>
/// Every notification about listing reports. Always called after the report is committed, and
/// never throws: a notification that fails is logged and the report stays saved.
///
/// What each side is told:
/// admins learn a report arrived (reference, listing title, reason; never the reporter's name);
/// the reporter learns when an admin asks them something and the general result when the report
/// closes (action taken / no violation found); never who reviewed it, the internal notes, or what
/// exactly was done to the listing or the account. The listing owner is not told about reports at
/// all; if an admin takes the listing down, the existing take-down notice tells the owner the
/// reason the admin wrote, with no reporter in it.
/// </summary>
public sealed class ListingReportNotifier
{
    private readonly INotificationService _notifications;
    private readonly IListingReportRepository _reports;
    private readonly ILogger<ListingReportNotifier> _logger;

    public ListingReportNotifier(
        INotificationService notifications, IListingReportRepository reports, ILogger<ListingReportNotifier> logger)
    {
        _notifications = notifications;
        _reports = reports;
        _logger = logger;
    }

    public async Task NotifyAdminsOfNewReportAsync(ListingReport report, CancellationToken ct)
    {
        IReadOnlyList<UserLabel> admins;
        try
        {
            admins = await _reports.ListAdminsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not list admins for a new listing report. ReportId={ReportId}", report.Id);
            return;
        }

        var key = report.Priority >= ListingReportPriority.High
            ? NotificationKeys.ListingReportSubmittedUrgent
            : NotificationKeys.ListingReportSubmitted;
        var message = NotificationText.Of(key,
            ("ref", report.ReferenceNumber),
            ("title", NotificationText.FromLiteral(report.ListingTitleSnapshot)),
            ("reason", NotificationText.Of(NotificationKeys.ListingReportReason(report.Reason))));

        foreach (var admin in admins.Where(a => a.Id != report.ReporterUserId))
        {
            await TrySendAsync(admin.Id, NotificationType.ListingReportSubmitted, message, report, $"lrs:{report.Id:N}", ct);
        }
    }

    public Task NotifyReporterInfoRequestedAsync(ListingReport report, Guid eventId, string question, CancellationToken ct)
        => TrySendAsync(
            report.ReporterUserId,
            NotificationType.ListingReportInfoRequested,
            NotificationText.Of(NotificationKeys.ListingReportInfoRequested,
                ("ref", report.ReferenceNumber),
                ("title", NotificationText.FromLiteral(report.ListingTitleSnapshot)),
                ("message", NotificationText.FromLiteral(question.Trim()))),
            report,
            $"lri:{eventId:N}",
            ct);

    public Task NotifyReporterClosedAsync(ListingReport report, CancellationToken ct)
        => TrySendAsync(
            report.ReporterUserId,
            NotificationType.ListingReportClosed,
            NotificationText.Of(
                report.Status == ListingReportStatus.Dismissed
                    ? NotificationKeys.ListingReportClosedNoViolation
                    : NotificationKeys.ListingReportClosedActionTaken,
                ("ref", report.ReferenceNumber),
                ("title", NotificationText.FromLiteral(report.ListingTitleSnapshot))),
            report,
            // One notice per closing: a retry of the same close is ignored, a close after a reopen is new.
            $"lrc:{report.Id:N}:{report.ReopenCount}",
            ct);

    private async Task TrySendAsync(
        Guid recipientId, NotificationType type, NotificationText message, ListingReport report, string dedupeKey, CancellationToken ct)
    {
        try
        {
            await _notifications.NotifyListingReportAsync(recipientId, type, message, report.Id, report.PropertyId, dedupeKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Listing report notification failed. Type={Type}, ReportId={ReportId}", type, report.Id);
        }
    }
}
