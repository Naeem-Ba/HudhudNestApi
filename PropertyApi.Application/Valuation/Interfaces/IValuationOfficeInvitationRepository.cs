using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Interfaces;

/// <summary>
/// Persistence for <see cref="ValuationOfficeInvitation"/>. See
/// <see cref="IValuationInquiryRepository"/>'s doc comment for why this exists now (Stage 5)
/// rather than Stage 4, where the invitations OfficeMatchingService builds stay in-memory.
/// </summary>
public interface IValuationOfficeInvitationRepository
{
    Task AddRangeAsync(IEnumerable<ValuationOfficeInvitation> invitations, CancellationToken ct = default);

    void Update(ValuationOfficeInvitation invitation);

    Task<IReadOnlyList<ValuationOfficeInvitation>> GetByInquiryIdAsync(
        Guid inquiryId,
        CancellationToken ct = default);

    /// <summary>
    /// Still-<see cref="ValuationOfficeInvitationStatus.Sent"/> invitations that no longer need
    /// (or can no longer expect) a response, resolved against their parent
    /// <see cref="ValuationInquiry"/> — <see cref="ValuationOfficeInvitation"/> declares no
    /// FK/navigation to its inquiry (see its own doc comment), so this join lives entirely in
    /// Infrastructure. Two independent reasons an invitation lands here, either being enough on
    /// its own:
    ///   1. The parent inquiry's own 24h <see cref="ValuationInquiry.ExpiresAt"/> has passed —
    ///      the invitation's own IsExpired(utcNow, expiresAt) takes exactly this cutoff, per
    ///      the Application-layer decision documented on ValuationSlaEnforcementService: one
    ///      unified 24h SLA clock per inquiry, not a separate per-invitation window.
    ///   2. The parent inquiry already reached a terminal status (Completed or Expired) by some
    ///      other path, regardless of ExpiresAt — otherwise an invitation whose inquiry finished
    ///      early (enough other offices already responded) would sit "Sent" forever, which is
    ///      exactly the "expired invitations can still be responded to" failure mode this
    ///      module's rules forbid.
    /// Ordered by the parent inquiry's ExpiresAt, capped by <paramref name="batchSize"/> —
    /// same backlog-draining reasoning as GetDueForExpiryAsync above.
    /// </summary>
    Task<IReadOnlyList<ValuationOfficeInvitation>> GetStaleSentInvitationsAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default);
}
