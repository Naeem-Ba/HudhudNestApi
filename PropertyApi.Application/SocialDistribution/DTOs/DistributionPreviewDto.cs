namespace PropertyApi.Application.SocialDistribution.DTOs;

/// <summary>
/// Read-only "what would happen" preview for one property (spec §18: "معاينة القواعد المطابقة
/// لعقار معين"). Produced by the same evaluation logic <c>DistributionEngine.RunAsync</c> uses,
/// but never creates a DistributionRun or any SocialPublication — see
/// <c>DistributionEngine.PreviewAsync</c>.
/// </summary>
public sealed record DistributionPreviewDto(
    Guid PropertyId,
    int MatchedRuleCount,
    IReadOnlyList<DistributionPreviewTargetDto> Targets);

/// <summary>One winning (rule, account) pair the property would be distributed to, or one skipped candidate with a human-readable reason (spec §20: "عرض سبب عدم مطابقة العقار للقاعدة").</summary>
public sealed record DistributionPreviewTargetDto(
    Guid SocialAccountId,
    string SocialAccountDisplayName,
    Guid WinningRuleId,
    string WinningRuleName,
    bool WouldPublish,
    string? SkipReason);
