using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Domain.Moderation;

/// <summary>
/// The fixed rules of listing reports: which reason starts at which priority, which reason needs
/// a written explanation, which outcome closes a report as Resolved or Dismissed, and the length
/// limits. Tunable numbers (daily limit, cooldown, review targets) live in configuration
/// (ListingReportOptions), not here.
/// </summary>
public static class ListingReportPolicy
{
    public const int MaxDetailsLength = 2000;
    public const int MinDetailsLengthWhenRequired = 10;
    public const int MaxNoteLength = 2000;
    public const int MaxTitleSnapshotLength = 200;

    /// <summary>
    /// Starting priority by reason. Money and identity come first: a fake owner or a request to pay
    /// outside the platform can cost a person their savings before anyone reviews the listing.
    /// This is a starting point for the queue only; an admin can change it, and no priority ever
    /// hides a listing or restricts an account by itself.
    /// </summary>
    public static ListingReportPriority DefaultPriority(ListingReportReason reason) => reason switch
    {
        ListingReportReason.Fraud => ListingReportPriority.High,
        ListingReportReason.Impersonation => ListingReportPriority.High,
        ListingReportReason.OffPlatformPayment => ListingReportPriority.High,
        ListingReportReason.FakeListing => ListingReportPriority.High,
        ListingReportReason.IllegalContent => ListingReportPriority.High,
        ListingReportReason.OffensiveContent => ListingReportPriority.High,
        ListingReportReason.Duplicate => ListingReportPriority.Low,
        ListingReportReason.NoLongerAvailable => ListingReportPriority.Low,
        _ => ListingReportPriority.Normal,
    };

    /// <summary>Reasons the admin cannot judge without the reporter's own words.</summary>
    public static bool RequiresDetails(ListingReportReason reason) => reason is ListingReportReason.Other;

    /// <summary>True for outcomes that close a report as Dismissed (nothing to act on).</summary>
    public static bool IsDismissal(ListingReportOutcome outcome) => outcome is
        ListingReportOutcome.NoViolation or
        ListingReportOutcome.InsufficientEvidence or
        ListingReportOutcome.DuplicateReport;

    public static bool IsOpen(ListingReportStatus status) => status is
        ListingReportStatus.New or
        ListingReportStatus.UnderReview or
        ListingReportStatus.WaitingForInformation or
        ListingReportStatus.ActionRequired;

    /// <summary>
    /// Open reports from different accounts on one listing at which the queue raises a report to
    /// High. A signal to look sooner, never a verdict: several accounts can coordinate against a
    /// competitor, which is why the count never hides anything by itself.
    /// </summary>
    public const int DistinctReportersForEscalation = 3;
}
