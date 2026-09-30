namespace HudhudNestApi.Domain.Investments.Enums;

/// <summary>
/// Investment project lifecycle. Enforced as a strict state machine on
/// <see cref="Entities.InvestmentProject"/> — no arbitrary status assignment (Phase 1 spec §5).
///
/// Primary path: Draft → UnderReview → Approved → Scheduled → Published → Closed.
/// Side states: Rejected (from UnderReview) and Suspended (from Published) — both dead ends
/// reachable only from the states named on their transition methods; see
/// <see cref="Entities.InvestmentProject"/> for the exact guards.
/// </summary>
public enum InvestmentProjectStatus
{
    Draft = 0,
    UnderReview = 1,
    Approved = 2,
    Scheduled = 3,
    Published = 4,
    Closed = 5,
    Rejected = 6,
    Suspended = 7,
}
