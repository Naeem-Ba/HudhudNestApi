using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Demand.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Domain.Demand.Entities;

/// <summary>
/// One listing matched to one search request (unique pair). Keeps the score and the reasons so the
/// requester sees why it was suggested; <see cref="RequesterNotifiedAt"/> and <see cref="OwnerNotifiedAt"/>
/// make each side hear about it at most once.
/// </summary>
public sealed class SearchRequestMatch : BaseEntity
{
    public Guid SearchRequestId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Property? Property { get; private set; }
    public int Score { get; private set; }

    /// <summary>Comma-separated reason codes, e.g. "location_neighborhood,budget_within,rooms_unknown".</summary>
    public string Reasons { get; private set; } = string.Empty;

    public SearchRequestMatchSource Source { get; private set; }
    public Guid? ProposedByUserId { get; private set; }
    public DateTime? ProposedAt { get; private set; }
    public DateTime? RequesterNotifiedAt { get; private set; }
    public DateTime? OwnerNotifiedAt { get; private set; }

    private SearchRequestMatch() { }

    public static SearchRequestMatch Create(Guid searchRequestId, Guid propertyId, int score, IEnumerable<string> reasons, DateTime nowUtc)
        => new()
        {
            SearchRequestId = searchRequestId,
            PropertyId = propertyId,
            Score = Math.Clamp(score, 0, 100),
            Reasons = string.Join(',', reasons),
            Source = SearchRequestMatchSource.Automatic,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

    public void Rescore(int score, IEnumerable<string> reasons, DateTime nowUtc)
    {
        Score = Math.Clamp(score, 0, 100);
        Reasons = string.Join(',', reasons);
        UpdatedAt = nowUtc;
    }

    public void MarkProposed(Guid byUserId, DateTime nowUtc)
    {
        Source = SearchRequestMatchSource.Proposed;
        ProposedByUserId = byUserId;
        ProposedAt ??= nowUtc;
        UpdatedAt = nowUtc;
    }

    public void MarkRequesterNotified(DateTime nowUtc) => RequesterNotifiedAt ??= nowUtc;

    public void MarkOwnerNotified(DateTime nowUtc) => OwnerNotifiedAt ??= nowUtc;

    public IReadOnlyList<string> ReasonCodes()
        => Reasons.Length == 0 ? [] : Reasons.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
