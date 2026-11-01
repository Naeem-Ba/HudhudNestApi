using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Demand.DTOs;
using HudhudNestApi.Application.Demand.Interfaces;
using HudhudNestApi.Application.Demand.Matching;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Application.Demand.Queries;

public sealed record GetMySearchRequestsQuery(Guid UserId) : IRequest<IReadOnlyList<SearchRequestDto>>;

public sealed record GetSearchRequestMatchesQuery(Guid RequestId, Guid UserId) : IRequest<IReadOnlyList<SearchRequestMatchDto>>;

/// <summary>The anonymous request board, for advertisers with at least one listing on the market.</summary>
public sealed record GetSearchRequestBoardQuery(Guid UserId, ListingType? ListingType, int? GovernorateId, int? PropertyTypeId, int Page = 1, int PageSize = 20)
    : IRequest<PagedSearchRequests>;

/// <summary>Requests that fit one of the caller's listings, best first.</summary>
public sealed record GetListingDemandQuery(Guid PropertyId, Guid UserId) : IRequest<IReadOnlyList<ListingDemandDto>>;

public sealed class SearchRequestQueryHandlers :
    IRequestHandler<GetMySearchRequestsQuery, IReadOnlyList<SearchRequestDto>>,
    IRequestHandler<GetSearchRequestMatchesQuery, IReadOnlyList<SearchRequestMatchDto>>,
    IRequestHandler<GetSearchRequestBoardQuery, PagedSearchRequests>,
    IRequestHandler<GetListingDemandQuery, IReadOnlyList<ListingDemandDto>>
{
    public const int MaxMatchesShown = 50;
    public const int MaxCandidatesPerListing = 300;
    public const int MaxDemandShown = 20;

    private readonly ISearchRequestRepository _repo;
    private readonly IPropertyOwnershipService _ownership;
    private readonly SearchRequestMatchingOptions _options;
    private readonly TimeProvider _clock;

    public SearchRequestQueryHandlers(
        ISearchRequestRepository repo,
        IPropertyOwnershipService ownership,
        IOptions<SearchRequestMatchingOptions> options,
        TimeProvider clock)
    {
        _repo = repo;
        _ownership = ownership;
        _options = options.Value;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<SearchRequestDto>> Handle(GetMySearchRequestsQuery q, CancellationToken ct)
    {
        var rows = await _repo.ListByUserAsync(q.UserId, _options.MinimumScore, ct);
        var now = Now;
        return rows.Select(r =>
        {
            // Shown as expired even before the sweep stamps it, so the page never says "active" past the date.
            r.Request.ExpireIfDue(now);
            return SearchRequestDto.From(r.Request, r.MatchCount);
        }).ToList();
    }

    public async Task<IReadOnlyList<SearchRequestMatchDto>> Handle(GetSearchRequestMatchesQuery q, CancellationToken ct)
    {
        _ = await _repo.GetOwnedAsync(q.RequestId, q.UserId, ct) ?? throw new NotFoundException("الطلب غير موجود.");
        return await _repo.GetMatchesAsync(q.RequestId, Now, _options.MinimumScore, MaxMatchesShown, ct);
    }

    public async Task<PagedSearchRequests> Handle(GetSearchRequestBoardQuery q, CancellationToken ct)
    {
        var now = Now;
        if (!await _repo.HasListingOnMarketAsync(q.UserId, now, ct))
            throw new ForbiddenException("لوحة الطلبات متاحة للمعلنين الذين لديهم إعلان منشور.", "SEARCH_REQUEST_BOARD_ADVERTISERS_ONLY");

        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 50);
        return await _repo.GetBoardAsync(q.ListingType, q.GovernorateId, q.PropertyTypeId, now, page, size, ct);
    }

    public async Task<IReadOnlyList<ListingDemandDto>> Handle(GetListingDemandQuery q, CancellationToken ct)
    {
        await _ownership.GetOwnedPropertyOrThrowAsync(q.PropertyId, q.UserId, "view-demand", ct);

        var now = Now;
        var facts = await _repo.GetListingFactsAsync(q.PropertyId, now, ct);
        if (facts is null)
            return [];

        var candidates = await _repo.GetCandidatesForListingAsync(facts, now, MaxCandidatesPerListing, ct);
        var rates = await _repo.LoadRatesAsync(ct);
        var proposed = await _repo.GetProposedRequestIdsAsync(q.PropertyId, ct);

        return candidates
            .Where(r => r.UserId != q.UserId)
            .Select(r => (Request: r, Result: SearchRequestMatcher.Evaluate(r, facts, rates, _options)))
            .Where(x => x.Result is not null && x.Result.Score >= _options.MinimumScore)
            .OrderByDescending(x => x.Result!.Score)
            .ThenByDescending(x => x.Request.CreatedAt)
            .Take(MaxDemandShown)
            .Select(x => new ListingDemandDto(AnonymousSearchRequestDto.From(x.Request), x.Result!.Score, x.Result.Reasons, proposed.Contains(x.Request.Id)))
            .ToList();
    }
}
