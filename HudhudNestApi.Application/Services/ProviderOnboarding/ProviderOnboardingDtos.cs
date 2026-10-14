using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.ProviderOnboarding;

public sealed record ServiceProviderEventDto(
    Guid Id,
    ServiceProviderEventType Type,
    string? Note,
    bool IsInternal,
    Guid? ActorUserId,
    Guid? ApplicationId,
    DateTime CreatedAt);

/// <summary>The applicant's own view: no internal notes, no admin identities.</summary>
public sealed record ServiceProviderApplicationDto(
    Guid Id,
    string ReferenceNumber,
    ServiceProviderApplicantType ApplicantType,
    string DisplayName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory> RequestedCategories,
    IReadOnlyList<int> RequestedGovernorateIds,
    string Experience,
    string? CompanyName,
    string? LicenseNumber,
    ServiceProviderApplicationStatus Status,
    string? InfoRequest,
    string? DecisionReason,
    Guid? ServiceProviderId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ServiceProviderEventDto> Timeline);

public sealed record AdminServiceProviderApplicationSummaryDto(
    Guid Id,
    string ReferenceNumber,
    Guid ApplicantUserId,
    ServiceProviderApplicantType ApplicantType,
    string DisplayName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory> RequestedCategories,
    IReadOnlyList<int> RequestedGovernorateIds,
    ServiceProviderApplicationStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Admin detail: everything, including internal notes and the applicant's account state.</summary>
public sealed record AdminServiceProviderApplicationDetailDto(
    AdminServiceProviderApplicationSummaryDto Summary,
    string Experience,
    string? CompanyName,
    string? LicenseNumber,
    string? InfoRequest,
    string? DecisionReason,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAt,
    DateTime TermsAcceptedAt,
    Guid? ServiceProviderId,
    string ApplicantAccountName,
    bool ApplicantAccountActive,
    Guid? ExistingProviderId,
    ServiceProviderStatus? ExistingProviderStatus,
    IReadOnlyList<ServiceProviderEventDto> Timeline);

public sealed record AdminServiceProviderSummaryDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? ContactEmail,
    string? ContactPhone,
    ServiceProviderStatus Status,
    ServiceProviderVerificationLevel VerificationLevel,
    IReadOnlyList<ServiceCategory> ApprovedCategories,
    IReadOnlyList<int> CoverageGovernorateIds,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AdminServiceProviderDetailDto(
    ServiceProviderDto Provider,
    int OpenRequestCount,
    IReadOnlyList<ServiceOfferingDto> Offerings,
    IReadOnlyList<AdminServiceProviderApplicationSummaryDto> Applications,
    IReadOnlyList<ServiceProviderEventDto> Timeline);

public sealed record ServicePageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ServiceCategoryAvailabilityDto(ServiceCategory Category, int OfferingCount);
