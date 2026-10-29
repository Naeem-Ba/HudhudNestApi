using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Moderation;

/// <summary>
/// Tunable numbers of listing reports, bound from the "ListingReports" configuration section.
/// The defaults are conservative starting values, not commitments: the review targets only decide
/// which open reports the admin page marks as overdue, and are never shown to reporters.
/// </summary>
public sealed class ListingReportOptions
{
    public const string SectionName = "ListingReports";

    /// <summary>Reports one account may send in 24 hours, across all listings.</summary>
    public int DailyLimitPerReporter { get; set; } = 10;

    /// <summary>
    /// After a report is closed, the same account may report the same listing for the same reason
    /// again only after this many days. A different reason is accepted at once: a new problem is a
    /// new report.
    /// </summary>
    public int SameReasonCooldownDays { get; set; } = 30;

    public int ReviewTargetHoursUrgent { get; set; } = 24;
    public int ReviewTargetHoursHigh { get; set; } = 48;
    public int ReviewTargetHoursNormal { get; set; } = 120;
    public int ReviewTargetHoursLow { get; set; } = 240;

    public TimeSpan ReviewTarget(ListingReportPriority priority) => TimeSpan.FromHours(priority switch
    {
        ListingReportPriority.Urgent => Positive(ReviewTargetHoursUrgent, 24),
        ListingReportPriority.High => Positive(ReviewTargetHoursHigh, 48),
        ListingReportPriority.Low => Positive(ReviewTargetHoursLow, 240),
        _ => Positive(ReviewTargetHoursNormal, 120),
    });

    /// <summary>The created-before cutoff per priority: an open report created before its cutoff is overdue.</summary>
    public IReadOnlyDictionary<ListingReportPriority, DateTime> OverdueCutoffs(DateTime utcNow) =>
        Enum.GetValues<ListingReportPriority>().ToDictionary(p => p, p => utcNow - ReviewTarget(p));

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;
}
