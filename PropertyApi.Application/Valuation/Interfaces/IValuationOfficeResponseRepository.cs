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
}
