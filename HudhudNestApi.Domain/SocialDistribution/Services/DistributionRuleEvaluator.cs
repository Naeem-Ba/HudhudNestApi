using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Domain.SocialDistribution.Services;

/// <summary>
/// The single, testable place a <see cref="DistributionRule"/> is compared against a property
/// (Phase 4 spec §10/§11 — "لا تنسخ هذا المنطق داخل أكثر من مكان"). Pure, static, no
/// infrastructure/ORM/HTTP dependency — <see cref="Application.SocialDistribution.Services.DistributionEngine"/>
/// (Application layer) is the only caller, and it is also what
/// <c>DistributionRuleEvaluatorTests</c> exercises directly without a database.
/// </summary>
public static class DistributionRuleEvaluator
{
    /// <summary>
    /// Spec §10's full matching predicate, restricted to what a single rule can decide on its
    /// own (account/channel eligibility is a separate, live check the caller performs — a rule
    /// has no reference to a SocialAccount's current Status). A null rule dimension always means
    /// "matches every value" — see DistributionRule's field docs.
    /// </summary>
    public static bool Matches(DistributionRule rule, DistributionCandidateProperty property, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(property);

        if (!rule.IsActive || rule.IsArchived)
            return false;

        if (!rule.IsWithinValidityWindow(utcNow))
            return false;

        if (rule.ProvinceId is { } provinceId && provinceId != property.GovernorateId)
            return false;

        if (rule.PropertyTypeId is { } propertyTypeId && propertyTypeId != property.PropertyTypeId)
            return false;

        if (rule.TransactionType is { } transactionType && transactionType != property.ListingType)
            return false;

        return true;
    }

    /// <summary>
    /// Picks the single winning rule among several rules that all matched the same property AND
    /// target the same SocialAccount (spec §11's default policy: "Group by SocialAccountId →
    /// Select the winning rule for each account → Create one Publication per SocialAccount").
    ///
    /// Ordering, most to least significant:
    /// 1. Explicit <see cref="DistributionRule.Priority"/>, descending — the operator's own
    ///    stated ranking always wins first; this is the one signal a human can reason about and
    ///    control without understanding the automatic scoring below it.
    /// 2. <see cref="DistributionRule.SpecificityScore"/>, descending — among equal-priority
    ///    rules, the one that pins down more of Province/PropertyType/TransactionType is
    ///    considered "more specific" and wins (spec §11's worked example: a general province
    ///    rule vs. a specific apartment-for-sale rule).
    /// 3. CreatedAt, descending — a newer rule reflects the operator's most recent intent.
    /// 4. Id, descending — a final, fully deterministic tiebreaker so ordering is never
    ///    ambiguous even for two rules created in the same instant (spec §11: "لا تجعل الترتيب
    ///    غامضاً").
    /// </summary>
    public static DistributionRule? SelectWinner(IEnumerable<DistributionRule> matchingRulesForSameAccount)
    {
        ArgumentNullException.ThrowIfNull(matchingRulesForSameAccount);

        return matchingRulesForSameAccount
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.SpecificityScore)
            .ThenByDescending(rule => rule.CreatedAt)
            .ThenByDescending(rule => rule.Id)
            .FirstOrDefault();
    }
}
