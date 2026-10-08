using HudhudNestApi.Domain.Reviews.Entities;

namespace HudhudNestApi.Application.Reviews.Interfaces;

/// <summary>Average of the four criteria per agency plus how many ratings it rests on.</summary>
public sealed record AgencyRatingAverages(
    double Service,
    double Professionalism,
    double Reliability,
    double OverallExperience,
    double Overall,
    int TotalCount);

public interface IAgencyRatingRepository
{
    Task AddAsync(AgencyRating rating, CancellationToken ct = default);

    Task<AgencyRating?> GetByRaterAndAgencyAsync(Guid agencyId, Guid raterId, CancellationToken ct = default);

    Task<AgencyRatingAverages> GetAveragesAsync(Guid agencyId, CancellationToken ct = default);

    /// <summary>Overall average and count for many agencies in one round trip (listing pages).</summary>
    Task<IReadOnlyDictionary<Guid, (double Overall, int Count)>> GetOverallSummariesAsync(
        IReadOnlyCollection<Guid> agencyIds,
        CancellationToken ct = default);

    Task<(IReadOnlyList<AgencyRating> Ratings, int TotalCount)> GetByAgencyAsync(
        Guid agencyId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// True when the rater really dealt with the agency: a completed visit to, or a message
    /// thread about, a listing currently attributed to that agency.
    /// </summary>
    Task<bool> HasInteractionWithAgencyAsync(Guid raterId, Guid agencyId, CancellationToken ct = default);
}
