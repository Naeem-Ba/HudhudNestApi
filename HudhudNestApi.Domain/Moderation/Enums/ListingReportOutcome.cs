namespace HudhudNestApi.Domain.Moderation.Enums;

/// <summary>
/// The decision recorded when a report is closed. The outcome decides the closed status:
/// the three first values close the report as Dismissed (no violation established), the others
/// as Resolved (something was done). A note explaining the decision is always required.
/// </summary>
public enum ListingReportOutcome
{
    /// <summary>Reviewed: the listing does not break the rules.</summary>
    NoViolation = 1,

    /// <summary>Reviewed: the report could not be confirmed with what is available.</summary>
    InsufficientEvidence = 2,

    /// <summary>The same problem is already handled in another report.</summary>
    DuplicateReport = 3,

    /// <summary>The listing's data was corrected (by the owner or an admin).</summary>
    ListingCorrected = 10,

    /// <summary>The owner was asked to correct the content.</summary>
    CorrectionRequested = 11,

    /// <summary>The listing was taken off the site with the existing admin take-down (reversible; the
    /// owner cannot re-publish it). Requires the listing to be taken down when the report is closed.</summary>
    ListingTakenDown = 12,

    /// <summary>Action was taken on the publisher's account through the admin user tools.</summary>
    AccountActioned = 13,

    /// <summary>Another action, described in the note.</summary>
    OtherAction = 14,
}
