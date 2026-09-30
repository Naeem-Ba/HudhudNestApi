using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

public interface IDistributionRuleRepository
{
    Task AddAsync(DistributionRule rule, CancellationToken ct = default);

    Task<DistributionRule?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<DistributionRule>> GetPagedAsync(DistributionRuleFilterDto filter, CancellationToken ct = default);

    /// <summary>
    /// DB-level pre-filter (spec §23: "استخدام Filtering على مستوى قاعدة البيانات") — returns only
    /// active, non-archived, currently-in-validity-window rules whose Province/PropertyType/
    /// TransactionType each either match the given property values or are null (wildcard).
    /// <see cref="Domain.SocialDistribution.Services.DistributionRuleEvaluator.Matches"/> is still
    /// run against the result before anything is trusted — this is a performance pre-filter, not
    /// a replacement for the single source of truth on matching semantics.
    /// </summary>
    Task<IReadOnlyList<DistributionRule>> GetActiveCandidatesAsync(
        int? governorateId,
        int? propertyTypeId,
        ListingType listingType,
        DateTime utcNow,
        CancellationToken ct = default);

    void Update(DistributionRule rule);
}
