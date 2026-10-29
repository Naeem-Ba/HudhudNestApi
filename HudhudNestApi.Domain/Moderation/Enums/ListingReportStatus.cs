namespace HudhudNestApi.Domain.Moderation.Enums;

/// <summary>
/// Where a report is in its review. The four first values are open, the two last are closed.
/// There is deliberately no "Reopened" status: reopening sends a closed report back to
/// <see cref="UnderReview"/>, and the reopening itself is an event in the report's timeline
/// (with who and why) plus <c>ListingReport.ReopenCount</c>. A separate status would be a
/// second name for "under review" that every filter and counter would have to treat the same.
/// </summary>
public enum ListingReportStatus
{
    New = 1,
    UnderReview = 2,
    WaitingForInformation = 3,
    ActionRequired = 4,
    Resolved = 5,
    Dismissed = 6,
}
