using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Views;

/// <summary>Scope "mine": the caller's own listings. Scope "agency": every listing under the caller's agency (agency owners only).</summary>
public sealed record GetListingViewStatsQuery(Guid UserId, bool IsAgencyOwner, string Scope) : IRequest<ListingViewStatsDto>;

public sealed class GetListingViewStatsQueryHandler : IRequestHandler<GetListingViewStatsQuery, ListingViewStatsDto>
{
    public const string ScopeMine = "mine";
    public const string ScopeAgency = "agency";

    private readonly IListingViewRepository _views;
    private readonly IListingViewPolicy _policy;

    public GetListingViewStatsQueryHandler(IListingViewRepository views, IListingViewPolicy policy)
    {
        _views = views;
        _policy = policy;
    }

    public async Task<ListingViewStatsDto> Handle(GetListingViewStatsQuery request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<ListingViewStatsRow> rows;

        if (string.Equals(request.Scope, ScopeAgency, StringComparison.OrdinalIgnoreCase))
        {
            if (!request.IsAgencyOwner)
                throw new ForbiddenException("Only an agency owner can see the agency's statistics.");

            var agencyId = await _views.GetUserAgencyIdAsync(request.UserId, ct)
                ?? throw new ForbiddenException("You do not belong to an agency.");
            rows = await _views.GetAgencyStatsAsync(agencyId, now, ct);
        }
        else
        {
            rows = await _views.GetOwnerStatsAsync(request.UserId, now, ct);
        }

        var items = rows
            .OrderByDescending(r => r.Views30Days)
            .ThenByDescending(r => r.TotalViews)
            .Select(r => new ListingViewStatsItemDto(
                r.Kind, r.ListingId, r.Title, r.TotalViews, r.Views7Days, r.Views30Days, r.UniqueViewers30Days))
            .ToList();

        var scope = string.Equals(request.Scope, ScopeAgency, StringComparison.OrdinalIgnoreCase) ? ScopeAgency : ScopeMine;
        return new ListingViewStatsDto(
            scope, now, _policy.RetentionDays,
            items.Sum(i => i.TotalViews), items.Sum(i => i.Views7Days), items.Sum(i => i.Views30Days), items);
    }
}

/// <summary>Daily trend of one listing. The caller must own it, own its agency, or be an admin.</summary>
public sealed record GetListingViewTrendQuery(
    ListingKind Kind, Guid ListingId, Guid UserId, bool IsAdmin, bool IsAgencyOwner, int Days)
    : IRequest<ListingViewTrendDto>;

public sealed class GetListingViewTrendQueryHandler : IRequestHandler<GetListingViewTrendQuery, ListingViewTrendDto>
{
    public const int MaxDays = 90;

    private readonly IListingViewRepository _views;
    private readonly IListingViewPolicy _policy;

    public GetListingViewTrendQueryHandler(IListingViewRepository views, IListingViewPolicy policy)
    {
        _views = views;
        _policy = policy;
    }

    public async Task<ListingViewTrendDto> Handle(GetListingViewTrendQuery request, CancellationToken ct)
    {
        var target = await _views.GetTargetAsync(request.Kind, request.ListingId, ct)
            ?? throw new NotFoundException("Listing was not found.");

        var allowed = request.IsAdmin || target.OwnerId == request.UserId;
        if (!allowed && request.IsAgencyOwner && target.AgencyId is { } agencyId)
            allowed = await _views.GetUserAgencyIdAsync(request.UserId, ct) == agencyId;

        if (!allowed)
            throw new ForbiddenException("You cannot see this listing's statistics.");

        var days = Math.Clamp(request.Days, 1, Math.Min(MaxDays, _policy.RetentionDays));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = today.AddDays(-(days - 1));
        var counts = await _views.GetDailyViewsAsync(
            request.Kind, request.ListingId, first.ToDateTime(TimeOnly.MinValue), ct);

        var points = Enumerable.Range(0, days)
            .Select(i => first.AddDays(i))
            .Select(d => new ListingViewTrendPointDto(d, counts.TryGetValue(d, out var n) ? n : 0))
            .ToList();

        var totals = await _views.GetCountsAsync(request.Kind, new[] { request.ListingId }, ct);
        return new ListingViewTrendDto(
            request.Kind, request.ListingId, days, totals.TryGetValue(request.ListingId, out var total) ? total : 0, points);
    }
}
