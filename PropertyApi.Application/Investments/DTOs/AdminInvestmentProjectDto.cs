using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.DTOs;

/// <summary>Admin/staff list row — includes Status across every lifecycle state (public list
/// only ever returns Published projects).</summary>
public sealed record AdminInvestmentProjectListDto(
    Guid Id,
    string Title,
    InvestmentProjectStatus Status,
    InvestmentProjectType ProjectType,
    Guid OwnerUserId,
    decimal TargetAmount,
    decimal RaisedAmount,
    DateTime CreatedAt,
    DateTime? PublishedAt);

/// <summary>Full staff review view of one project — includes internal-only fields (rejection
/// reason, readiness checklist) that never appear in the public DTO.</summary>
public sealed record InvestmentProjectReviewDto(
    InvestmentProjectDetailsDto Project,
    string? RejectionReason,
    InvestmentFinancialSummaryDto? Financials,
    InvestmentRiskDto? Risk,
    int PublicDocumentCount,
    InvestmentProjectPublishReadinessDto Readiness);

/// <summary>Explicit checklist mirroring the Publish-readiness rules in Phase 1 spec §17 — lets
/// the admin UI show exactly what is missing before a project can be published.</summary>
public sealed record InvestmentProjectPublishReadinessDto(
    bool HasTitleAndDescription,
    bool HasFinancials,
    bool HasRiskAssessment,
    bool HasRequiredDocuments,
    bool IsReady);
