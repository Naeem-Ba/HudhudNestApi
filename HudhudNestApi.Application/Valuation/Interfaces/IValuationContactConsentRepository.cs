using HudhudNestApi.Domain.Valuation.Entities;

namespace HudhudNestApi.Application.Valuation.Interfaces;

/// <summary>Persistence for <see cref="ValuationContactConsent"/> (Stage 9).</summary>
public interface IValuationContactConsentRepository
{
    Task AddAsync(ValuationContactConsent consent, CancellationToken ct = default);

    /// <summary>
    /// At most one consent ever exists per invitation (see
    /// ValuationContactConsentConfiguration's unique index) — used both by
    /// SubmitValuationContactConsentCommandHandler (idempotency: re-submitting the same
    /// invitation's consent must not create a duplicate row) and by the office dashboard query
    /// (does this specific invitation have consent yet?).
    /// </summary>
    Task<ValuationContactConsent?> GetByInvitationIdAsync(Guid invitationId, CancellationToken ct = default);

    /// <summary>
    /// Every consent granted for this agency, across all its invitations — used by the office
    /// dashboard's list query (Stage 6's GetMyAgencyValuationInquiriesQuery) to resolve, in one
    /// round trip, which of the agency's own invitations already have consent, instead of one
    /// GetByInvitationIdAsync call per row.
    /// </summary>
    Task<IReadOnlyList<ValuationContactConsent>> GetByAgencyIdAsync(Guid agencyId, CancellationToken ct = default);
}
