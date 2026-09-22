namespace HudhudNestApi.Domain.Investments.Enums;

/// <summary>
/// Status of an <see cref="Entities.InvestmentInterest"/> — an Expression of Interest, never a
/// real investment, payment, or commitment (Phase 1 spec §12).
/// </summary>
public enum InvestmentInterestStatus
{
    Active = 0,
    Withdrawn = 1,

    /// <summary>
    /// Reserved for a future CRM-style workflow where staff mark an interest as followed up on.
    /// No command/handler sets this in Phase 1 — see Phase 1 spec §47 (extension points only,
    /// no business logic for out-of-scope functionality).
    /// </summary>
    Contacted = 2,
}
