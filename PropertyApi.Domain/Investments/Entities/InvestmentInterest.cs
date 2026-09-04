using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Domain.Investments.Entities;

/// <summary>
/// An Expression of Interest — a user saying "I'd like to invest" about a project. This is
/// deliberately NOT named "Investment": it never records money, a commitment, or a contract.
/// Creating one has exactly one effect — a row exists recording that this user is interested in
/// this project. See Phase 1 spec §12 and §30.
/// </summary>
public sealed class InvestmentInterest : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid InvestmentProjectId { get; private set; }
    public InvestmentInterestStatus Status { get; private set; }

    private InvestmentInterest() { }

    public static InvestmentInterest Create(Guid userId, Guid investmentProjectId)
    {
        if (userId == Guid.Empty)
            throw new DomainException("المستخدم مطلوب.");
        if (investmentProjectId == Guid.Empty)
            throw new DomainException("مشروع الاستثمار مطلوب.");

        return new InvestmentInterest
        {
            UserId = userId,
            InvestmentProjectId = investmentProjectId,
            Status = InvestmentInterestStatus.Active,
        };
    }

    public void Withdraw()
    {
        if (Status != InvestmentInterestStatus.Active)
            throw new DomainException($"لا يمكن سحب اهتمام في الحالة '{Status}'.");

        Status = InvestmentInterestStatus.Withdrawn;
    }

    /// <summary>Re-expressing interest after a withdrawal reactivates the same row instead of
    /// inserting a duplicate — keeps the UserId+InvestmentProjectId uniqueness constraint
    /// meaningful (Phase 1 spec §31 idempotency).</summary>
    public void Reactivate()
    {
        if (Status != InvestmentInterestStatus.Withdrawn)
            throw new DomainException($"لا يمكن إعادة تفعيل اهتمام في الحالة '{Status}'.");

        Status = InvestmentInterestStatus.Active;
    }
}
