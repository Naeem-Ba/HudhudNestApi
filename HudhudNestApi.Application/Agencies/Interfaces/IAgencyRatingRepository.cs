using HudhudNestApi.Domain.Agencies.Entities;

namespace HudhudNestApi.Application.Agencies.Interfaces;

/// <summary>Per-criterion averages for an office; all 0 when it has no ratings.</summary>
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

    /// <summary>The rater's own rating of the office, tracked so it can be edited in place.</summary>
    Task<AgencyRating?> GetByRaterAndAgencyAsync(Guid agencyId, Guid raterId, CancellationToken ct = default);

    Task<AgencyRatingAverages> GetAveragesAsync(Guid agencyId, CancellationToken ct = default);

    Task<(IReadOnlyList<AgencyRating> Ratings, int TotalCount)> GetPageAsync(
        Guid agencyId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// True when the rater had a real interaction with the office: a completed visit to one of its
    /// listings, or a message exchanged about one of its listings.
    /// </summary>
    Task<bool> HasInteractionAsync(Guid agencyId, Guid raterId, CancellationToken ct = default);
}
