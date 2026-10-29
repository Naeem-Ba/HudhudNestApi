using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Moderation;

// ── Reporter side ───────────────────────────────────────────────────────
// What the reporter sees of their own report. It never carries the admin's identity, internal
// notes, the closing note, the listing owner or other people's reports.

/// <summary>
/// The reporter-facing status. Collapses the internal workflow to what a reporter can act on:
/// Submitted, InReview, NeedsYourInfo, Closed.
/// </summary>
public enum ReporterReportStatus
{
    Submitted = 1,
    InReview = 2,
    NeedsYourInfo = 3,
    Closed = 4,
}

/// <summary>What a reporter is told about the result: something was done, or nothing was found.</summary>
public enum ReporterReportOutcome
{
    ActionTaken = 1,
    NoViolationFound = 2,
}

public sealed record ListingReportReasonDto(ListingReportReason Code, bool RequiresDetails, int MaxDetailsLength);

public sealed record MyListingReportDto(
    Guid Id,
    string ReferenceNumber,
    Guid PropertyId,
    string ListingTitle,
    ListingReportReason Reason,
    string? Details,
    ReporterReportStatus Status,
    ReporterReportOutcome? Outcome,
    string? InfoRequest,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    IReadOnlyList<MyListingReportEventDto> Timeline);

/// <summary>Only InfoRequested and ReporterResponded rows (the conversation with the reporter), never internal ones.</summary>
public sealed record MyListingReportEventDto(ListingReportEventType Type, string? Note, DateTime CreatedAt);

/// <summary>Whether the caller already has an open report on a listing (so the dialog can say so up front).</summary>
public sealed record MyOpenReportStatusDto(bool HasOpenReport, Guid? ReportId, string? ReferenceNumber, ReporterReportStatus? Status);

// ── Admin side ──────────────────────────────────────────────────────────

public sealed record AdminListingReportSummaryDto(
    Guid Id,
    string ReferenceNumber,
    Guid PropertyId,
    string ListingTitle,
    ListingType? ListingType,
    bool ListingDeleted,
    bool ListingTakenDown,
    ListingReportReason Reason,
    ListingReportStatus Status,
    ListingReportPriority Priority,
    bool IsOverdue,
    Guid ReporterUserId,
    string ReporterName,
    Guid? AssignedAdminId,
    string? AssignedAdminName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ListingReportPageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record AdminListingReportStatsDto(
    int Total,
    int New,
    int UnderReview,
    int WaitingForInformation,
    int ActionRequired,
    int Open,
    int Resolved,
    int Dismissed,
    int HighPriorityOpen,
    int Overdue,
    double? AverageResolutionHours,
    int ResolvedSampleSize);

public sealed record AdminListingSnapshotDto(
    Guid Id,
    string CurrentTitle,
    ListingType ListingType,
    string Status,
    bool IsPublished,
    bool IsTakenDown,
    string? TakenDownReason,
    bool IsDeleted,
    bool IsPubliclyVisible,
    decimal? Price,
    string CurrencyCode,
    string? City,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    Guid OwnerId,
    string OwnerName);

public sealed record AdminReporterDto(Guid Id, string Name, int TotalReports, int DismissedReports);

public sealed record AdminRelatedReportDto(
    Guid Id,
    string ReferenceNumber,
    ListingReportReason Reason,
    ListingReportStatus Status,
    ListingReportPriority Priority,
    bool SameReporter,
    DateTime CreatedAt);

public sealed record AdminListingReportEventDto(
    Guid Id,
    ListingReportEventType Type,
    Guid? ActorUserId,
    string? ActorName,
    string? FromValue,
    string? ToValue,
    string? Note,
    bool IsInternal,
    DateTime CreatedAt);

public sealed record AdminListingReportDetailDto(
    AdminListingReportSummaryDto Summary,
    string ListingTitleAtReport,
    string? Details,
    string? InfoRequest,
    ListingReportOutcome? Outcome,
    string? ResolutionNote,
    string? ResolvedByName,
    DateTime? ResolvedAt,
    DateTime? FirstReviewedAt,
    int ReopenCount,
    DateTime ReviewDueAt,
    AdminListingSnapshotDto? Listing,
    AdminReporterDto Reporter,
    int OpenReportsOnListing,
    int DistinctOpenReportersOnListing,
    IReadOnlyList<AdminRelatedReportDto> RelatedReports,
    IReadOnlyList<AdminListingReportEventDto> Timeline);
