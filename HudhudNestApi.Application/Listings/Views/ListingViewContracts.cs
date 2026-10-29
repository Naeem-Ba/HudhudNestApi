using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Views;

/// <summary>Result of a "the detail page was opened" report.</summary>
/// <param name="Counted">True when this request added one to the listing's total.</param>
/// <param name="ViewCount">The listing's total after the request.</param>
/// <param name="Reason">Why nothing was added: <c>cooldown</c>, <c>own-listing</c>, <c>automated</c> or <c>test-traffic</c>; null when counted.</param>
public sealed record ListingViewResultDto(bool Counted, long ViewCount, string? Reason);

/// <summary>What the view counter needs to know about a listing, read in one query.</summary>
public sealed record ListingViewTarget(
    Guid OwnerId,
    Guid? AgencyId,
    string Title,
    bool IsPubliclyViewable);

public sealed record ListingViewStatsItemDto(
    ListingKind Kind,
    Guid ListingId,
    string Title,
    long TotalViews,
    int Views7Days,
    int Views30Days,
    int UniqueViewers30Days);

public sealed record ListingViewStatsDto(
    string Scope,
    DateTime GeneratedAt,
    int HistoryRetentionDays,
    long TotalViews,
    int Views7Days,
    int Views30Days,
    IReadOnlyList<ListingViewStatsItemDto> Items);

public sealed record ListingViewTrendPointDto(DateOnly Date, int Views);

public sealed record ListingViewTrendDto(
    ListingKind Kind,
    Guid ListingId,
    int Days,
    long TotalViews,
    IReadOnlyList<ListingViewTrendPointDto> Points);

/// <summary>Row from the stats query before titles/scope are applied.</summary>
public sealed record ListingViewStatsRow(
    ListingKind Kind,
    Guid ListingId,
    string Title,
    long TotalViews,
    int Views7Days,
    int Views30Days,
    int UniqueViewers30Days);
