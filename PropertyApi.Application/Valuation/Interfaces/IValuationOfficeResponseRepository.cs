using PropertyApi.Domain.Valuation.Entities;

namespace PropertyApi.Application.Valuation.Interfaces;

/// <summary>
/// Persistence for <see cref="ValuationOfficeResponse"/> — the one Valuation entity Stage 5
/// deliberately left unpersisted ("nothing persists one until the Stage 6 office-response
/// handler exists", see AppDbContext's own comment on its DbSet region). This is that handler.
/// </summary>
public interface IValuationOfficeResponseRepository
{
    Task AddAsync(ValuationOfficeResponse response, CancellationToken ct = default);

    /// <summary>
    /// At most one response ever exists per invitation (see
    /// ValuationOfficeResponseConfiguration's unique index) — used by Stage 6's dashboard
    /// query to show an office its own submitted estimate alongside the invitation row.
    /// </summary>
    Task<ValuationOfficeResponse?> GetByInvitationIdAsync(Guid invitationId, CancellationToken ct = default);

    /// <summary>
    /// Stage 8 (Admin Dashboard) — per-agency count of responses submitted at or before their
    /// invitation's applicable deadline (the parent ValuationInquiry's ExpiresAt — the same
    /// unified 24h SLA clock SubmitOfficeResponseCommandHandler and
    /// ValuationOfficeInvitation.IsExpired already use), keyed by agency. Computed by an
    /// explicit database-side join/filter rather than assumed equal to the invitation's total
    /// response count — see AdminValuationInquiryService's doc comment for why, under this
    /// module's current write-path rules, every persisted response already satisfies this
    /// filter (a late one is rejected before it can ever be saved), so the two numbers
    /// coincide today without this method silently hard-coding that assumption.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetWithinSlaResponseCountsByAgencyAsync(
        CancellationToken ct = default);
}
