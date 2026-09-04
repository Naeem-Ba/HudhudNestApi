using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.Investments.Entities;

/// <summary>
/// A user's saved investment project — the Investment-module equivalent of
/// <c>Listings.Entities.Favorite</c>, kept separate because Favorite is hard-wired to Property
/// (Phase 1 spec §11). Removing an item is a genuine hard delete (no soft-delete fields), same
/// as Favorite — re-adding after removal is just a fresh insert.
/// </summary>
public sealed class InvestmentWatchlistItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid InvestmentProjectId { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private InvestmentWatchlistItem() { }

    public static InvestmentWatchlistItem Create(Guid userId, Guid investmentProjectId)
    {
        if (userId == Guid.Empty)
            throw new DomainException("المستخدم مطلوب.");
        if (investmentProjectId == Guid.Empty)
            throw new DomainException("مشروع الاستثمار مطلوب.");

        return new InvestmentWatchlistItem
        {
            UserId = userId,
            InvestmentProjectId = investmentProjectId,
        };
    }
}
