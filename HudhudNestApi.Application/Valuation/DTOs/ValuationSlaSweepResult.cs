namespace HudhudNestApi.Application.Valuation.DTOs;

/// <summary>
/// Summary of one <see cref="Interfaces.IValuationSlaEnforcementService.RunSweepAsync"/> pass —
/// returned (rather than void) so ValuationInquiryExpiryHostedService can log a single-line
/// summary the same way ListingExpiryHostedService/AccountDeletionSweepHostedService do, and so
/// tests can assert on outcome counts directly instead of re-querying repositories afterward.
/// </summary>
public sealed class ValuationSlaSweepResult
{
    public int InquiriesExpired { get; init; }

    public int InvitationsExpired { get; init; }

    /// <summary>Remediation M4 — CreatedAt+18h reminders successfully delivered this sweep.</summary>
    public int RemindersSent { get; init; }

    /// <summary>Remediation M3 — previously-failed notifications (of any of the Valuation
    /// module's notification types) successfully delivered on retry this sweep.</summary>
    public int NotificationsRetried { get; init; }
}
