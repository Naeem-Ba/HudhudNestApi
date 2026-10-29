using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Moderation;

internal static class ListingReportViews
{
    /// <summary>The only events a reporter sees: the questions asked of them and their answers.</summary>
    private static readonly HashSet<ListingReportEventType> ReporterVisibleEvents =
    [
        ListingReportEventType.InfoRequested,
        ListingReportEventType.ReporterResponded,
    ];

    public static ReporterReportStatus ReporterStatus(ListingReportStatus status) => status switch
    {
        ListingReportStatus.New => ReporterReportStatus.Submitted,
        ListingReportStatus.WaitingForInformation => ReporterReportStatus.NeedsYourInfo,
        ListingReportStatus.Resolved or ListingReportStatus.Dismissed => ReporterReportStatus.Closed,
        _ => ReporterReportStatus.InReview,
    };

    public static ReporterReportOutcome? ReporterOutcome(ListingReportStatus status) => status switch
    {
        ListingReportStatus.Resolved => ReporterReportOutcome.ActionTaken,
        ListingReportStatus.Dismissed => ReporterReportOutcome.NoViolationFound,
        _ => null,
    };

    public static async Task<MyListingReportDto> ForReporterAsync(
        ListingReport report, IListingReportRepository reports, CancellationToken ct)
    {
        var events = await reports.ListEventsAsync(report.Id, includeInternal: false, ct);
        var timeline = events
            .Where(e => !e.IsInternal && ReporterVisibleEvents.Contains(e.Type))
            .Select(e => new MyListingReportEventDto(e.Type, e.Note, e.CreatedAt))
            .ToList();

        return new MyListingReportDto(
            report.Id,
            report.ReferenceNumber,
            report.PropertyId,
            report.ListingTitleSnapshot,
            report.Reason,
            report.Details,
            ReporterStatus(report.Status),
            ReporterOutcome(report.Status),
            report.Status == ListingReportStatus.WaitingForInformation ? report.InfoRequest : null,
            report.CreatedAt,
            report.UpdatedAt,
            report.IsOpen ? null : report.ResolvedAt,
            timeline);
    }
}
