using PropertyApi.Domain.Enums;

namespace PropertyApi.Domain.SocialDistribution.Models;

/// <summary>
/// The minimal, read-only snapshot of a property that <see cref="Services.DistributionRuleEvaluator"/>
/// needs to decide whether a <see cref="Entities.DistributionRule"/> matches it — deliberately NOT
/// the <c>Property</c> entity itself (spec: "لا تعتمد [Domain] على ... ORM Concrete Classes" and the
/// bounded-context isolation principle carried over from Phase 3: SocialDistribution's Domain layer
/// must not reference the Listings aggregate). <see cref="ListingType"/> is the one exception —
/// it is a plain, behavior-free enum living in the shared <c>PropertyApi.Domain.Enums</c>
/// namespace (not a Listings entity/aggregate), so referencing it here does not reintroduce a
/// dependency on the Listings bounded context, only on a shared vocabulary value — the same way
/// <c>SocialAccount.GovernorateId</c> reuses the Governorate lookup's raw id without a navigation
/// property back into Lookups.
/// </summary>
public sealed record DistributionCandidateProperty(
    Guid PropertyId,
    int? GovernorateId,
    int? PropertyTypeId,
    ListingType ListingType);
