using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.ProviderOnboarding;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Admin;

/// <summary>Filters of the admin "service requests" list. Paging is clamped by the handler.</summary>
public sealed record AdminServiceRequestFilter(
    string? Search = null,
    ServiceRequestStatus? Status = null,
    ServiceCategory? Category = null,
    Guid? OfferingId = null,
    Guid? ProviderId = null,
    DateTime? From = null,
    DateTime? To = null,
    bool NeedsAttentionOnly = false,
    int Page = 1,
    int PageSize = 20,
    bool? Confirmed = null);

public sealed record AdminServiceRequestSummaryDto(
    Guid Id,
    string RequestNumber,
    Guid PropertyId,
    string PropertyTitle,
    string RequesterName,
    Guid ServiceProviderId,
    string ServiceProviderName,
    Guid ServiceOfferingId,
    string ServiceOfferingTitle,
    ServiceCategory Category,
    ServiceRequestStatus Status,
    ServiceOutcomeReason? OutcomeReason,
    ServiceRequesterAnswer? RequesterAnswer,
    ServiceRequesterReason? RequesterReason,
    int? Rating,
    bool NeedsAttention,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    ServiceRequestIntent? Intent = null,
    DateTime? ConfirmedAt = null,
    DateTime? LastActivityAt = null,
    ServiceParty? ExpiredWaitingOn = null,
    int MessageCount = 0);

public sealed record AdminServiceReviewDto(
    Guid Id,
    Guid ServiceRequestId,
    string ReviewerName,
    int Rating,
    string? Comment,
    bool IsHidden,
    string? HiddenReason,
    DateTime CreatedAt);

public sealed record AdminServiceRequestDetailDto(
    ServiceRequestDto Request,
    IReadOnlyList<ServiceRequestStatusHistoryDto> History,
    AdminServiceReviewDto? Review,
    bool NeedsAttention,
    IReadOnlyList<ServiceRequestMessageDto>? Messages = null);

public sealed record ReasonCountDto(string Reason, int Count);

/// <summary>One line of the statistics table: a category, an offering or a provider.</summary>
public sealed record ServiceStatsRowDto(
    string Key,
    Guid? Id,
    string? Label,
    string? SubLabel,
    bool? IsActive,
    int Requested,
    int Open,
    int Completed,
    int NotCompleted,
    int Rejected,
    int Cancelled,
    int Disputed,
    double? AverageRating,
    int RatingCount,
    int Inquiries = 0,
    int OpenInquiries = 0,
    int InquiriesDropped = 0,
    int Expired = 0)
{
    /// <summary>Requests that ended one way or the other.</summary>
    public int Closed => Completed + NotCompleted + Rejected + Cancelled + Expired;

    /// <summary>Confirmed requests / all conversations. Null until someone made contact.</summary>
    public double? ConversionRate => Inquiries == 0 ? null : (double)Requested / Inquiries;

    /// <summary>Completed / closed. Null until something closed, so an empty row never shows 0%.</summary>
    public double? CompletionRate => Closed == 0 ? null : (double)Completed / Closed;
}

public sealed record ServiceStatsDto(
    DateTime? From,
    DateTime? To,
    ServiceStatsRowDto Totals,
    IReadOnlyList<ServiceStatsRowDto> ByCategory,
    IReadOnlyList<ServiceStatsRowDto> ByOffering,
    IReadOnlyList<ServiceStatsRowDto> ByProvider,
    IReadOnlyList<ReasonCountDto> ProviderReasons,
    IReadOnlyList<ReasonCountDto> RequesterReasons,
    IReadOnlyList<ReasonCountDto>? ExpiryReasons = null);
