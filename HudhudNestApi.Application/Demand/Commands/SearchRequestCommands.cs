using System.Text.Json.Serialization;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Demand.DTOs;
using HudhudNestApi.Application.Demand.Interfaces;
using HudhudNestApi.Application.Demand.Matching;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Application.Demand.Commands;

/// <summary>The fields a requester fills in; shared by create and update so both are validated alike.</summary>
public interface ISearchRequestInput
{
    ListingType ListingType { get; }
    int GovernorateId { get; }
    int? DistrictId { get; }
    int? NeighborhoodId { get; }
    int? PropertyTypeId { get; }
    decimal? MinBudget { get; }
    decimal MaxBudget { get; }
    string CurrencyCode { get; }
    int? MinRooms { get; }
    decimal? MinArea { get; }
    decimal? MaxArea { get; }
    bool NeedsBalcony { get; }
    bool NeedsElevator { get; }
    bool NeedsParking { get; }
    bool NeedsFurnished { get; }
    string? Details { get; }
    DateTime? ExpiresAt { get; }
}

public sealed record CreateSearchRequestCommand(
    ListingType ListingType,
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? MinBudget,
    decimal MaxBudget,
    string CurrencyCode,
    int? MinRooms,
    decimal? MinArea,
    decimal? MaxArea,
    bool NeedsBalcony,
    bool NeedsElevator,
    bool NeedsParking,
    bool NeedsFurnished,
    string? Details,
    DateTime? ExpiresAt) : IRequest<SearchRequestDto>, ISearchRequestInput
{
    [JsonIgnore] public Guid UserId { get; init; }
}

public sealed record UpdateSearchRequestCommand(
    ListingType ListingType,
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? MinBudget,
    decimal MaxBudget,
    string CurrencyCode,
    int? MinRooms,
    decimal? MinArea,
    decimal? MaxArea,
    bool NeedsBalcony,
    bool NeedsElevator,
    bool NeedsParking,
    bool NeedsFurnished,
    string? Details,
    DateTime? ExpiresAt) : IRequest<SearchRequestDto>, ISearchRequestInput
{
    [JsonIgnore] public Guid Id { get; init; }
    [JsonIgnore] public Guid UserId { get; init; }
}

public enum SearchRequestAction { Pause, Resume, Close, Renew }

public sealed record ChangeSearchRequestStatusCommand(Guid Id, Guid UserId, SearchRequestAction Action, DateTime? ExpiresAt = null)
    : IRequest<SearchRequestDto>;

public sealed record ProposeListingCommand(Guid SearchRequestId, Guid PropertyId) : IRequest<Unit>
{
    [JsonIgnore] public Guid UserId { get; init; }
}

public sealed class SearchRequestCommandHandlers :
    IRequestHandler<CreateSearchRequestCommand, SearchRequestDto>,
    IRequestHandler<UpdateSearchRequestCommand, SearchRequestDto>,
    IRequestHandler<ChangeSearchRequestStatusCommand, SearchRequestDto>,
    IRequestHandler<ProposeListingCommand, Unit>
{
    /// <summary>Open requests (active or paused) per user. Each one is scanned by the matcher on every sweep.</summary>
    public const int MaxOpenRequestsPerUser = 5;

    /// <summary>Proposals an advertiser may send per 24 hours, so the board cannot be used to spam requesters.</summary>
    public const int MaxProposalsPerDay = 20;

    private readonly ISearchRequestRepository _repo;
    private readonly IPropertyOwnershipService _ownership;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _uow;
    private readonly SearchRequestMatchingOptions _options;
    private readonly TimeProvider _clock;

    public SearchRequestCommandHandlers(
        ISearchRequestRepository repo,
        IPropertyOwnershipService ownership,
        INotificationService notifications,
        IUnitOfWork uow,
        IOptions<SearchRequestMatchingOptions> options,
        TimeProvider clock)
    {
        _repo = repo;
        _ownership = ownership;
        _notifications = notifications;
        _uow = uow;
        _options = options.Value;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<SearchRequestDto> Handle(CreateSearchRequestCommand c, CancellationToken ct)
    {
        if (await _repo.CountOpenByUserAsync(c.UserId, ct) >= MaxOpenRequestsPerUser)
            throw new ConflictException(
                $"لديك {MaxOpenRequestsPerUser} طلبات مفتوحة. أغلق طلبًا قديمًا لإضافة طلب جديد.", "SEARCH_REQUEST_LIMIT");

        var request = PropertySearchRequest.Create(
            c.UserId, c.ListingType, c.GovernorateId, c.DistrictId, c.NeighborhoodId, c.PropertyTypeId,
            c.MinBudget, c.MaxBudget, c.CurrencyCode, c.MinRooms, c.MinArea, c.MaxArea,
            c.NeedsBalcony, c.NeedsElevator, c.NeedsParking, c.NeedsFurnished, c.Details, ToUtc(c.ExpiresAt), Now);

        _repo.Add(request);
        await _uow.SaveChangesAsync(ct);

        // Matching runs in the background sweep, never inside this request (brief §27).
        return SearchRequestDto.From(request, 0);
    }

    public async Task<SearchRequestDto> Handle(UpdateSearchRequestCommand c, CancellationToken ct)
    {
        var request = await _repo.GetOwnedAsync(c.Id, c.UserId, ct)
            ?? throw new NotFoundException("الطلب غير موجود.");

        request.Update(
            c.ListingType, c.GovernorateId, c.DistrictId, c.NeighborhoodId, c.PropertyTypeId,
            c.MinBudget, c.MaxBudget, c.CurrencyCode, c.MinRooms, c.MinArea, c.MaxArea,
            c.NeedsBalcony, c.NeedsElevator, c.NeedsParking, c.NeedsFurnished, c.Details, ToUtc(c.ExpiresAt), Now);

        await _uow.SaveChangesAsync(ct);
        return SearchRequestDto.From(request, 0);
    }

    public async Task<SearchRequestDto> Handle(ChangeSearchRequestStatusCommand c, CancellationToken ct)
    {
        var request = await _repo.GetOwnedAsync(c.Id, c.UserId, ct)
            ?? throw new NotFoundException("الطلب غير موجود.");

        var now = Now;
        request.ExpireIfDue(now);

        switch (c.Action)
        {
            case SearchRequestAction.Pause: request.Pause(now); break;
            case SearchRequestAction.Resume: request.Resume(now); break;
            case SearchRequestAction.Close: request.Close(now); break;
            case SearchRequestAction.Renew:
                if (await _repo.CountOpenByUserAsync(c.UserId, ct) >= MaxOpenRequestsPerUser)
                    throw new ConflictException(
                        $"لديك {MaxOpenRequestsPerUser} طلبات مفتوحة. أغلق طلبًا قديمًا لإضافة طلب جديد.", "SEARCH_REQUEST_LIMIT");
                request.Renew(ToUtc(c.ExpiresAt), now);
                break;
        }

        await _uow.SaveChangesAsync(ct);
        return SearchRequestDto.From(request, 0);
    }

    /// <summary>
    /// The advertiser offers one of their listings for an anonymous request. The requester gets a notification
    /// and decides whether to make contact; the advertiser learns nothing about the requester.
    /// </summary>
    public async Task<Unit> Handle(ProposeListingCommand c, CancellationToken ct)
    {
        var now = Now;

        // Throws 404/403 for a listing the caller does not own or manage (same rule as editing it).
        await _ownership.GetOwnedPropertyOrThrowAsync(c.PropertyId, c.UserId, "propose", ct);

        var request = await _repo.GetActiveAsync(c.SearchRequestId, now, ct)
            ?? throw new NotFoundException("الطلب غير موجود أو لم يعد نشطًا.");

        if (request.UserId == c.UserId)
            throw new ConflictException("لا يمكنك اقتراح عقار على طلبك أنت.", "SEARCH_REQUEST_OWN");

        var facts = await _repo.GetListingFactsAsync(c.PropertyId, now, ct)
            ?? throw new ConflictException("العقار غير منشور حاليًا.", "LISTING_NOT_ON_MARKET");

        var rates = await _repo.LoadRatesAsync(ct);
        var result = SearchRequestMatcher.Evaluate(request, facts, rates, _options)
            ?? throw new ConflictException("هذا العقار لا يطابق شروط الطلب (النوع أو المحافظة أو الميزانية أو المساحة).", "LISTING_DOES_NOT_FIT");

        var existing = await _repo.GetMatchAsync(request.Id, c.PropertyId, ct);
        if (existing?.ProposedAt is not null)
            return Unit.Value; // idempotent: proposing twice changes nothing and sends nothing

        if (await _repo.CountProposalsSinceAsync(c.UserId, now.AddDays(-1), ct) >= MaxProposalsPerDay)
            throw new TooManyRequestsException(
                $"وصلت إلى حد الاقتراحات اليومي ({MaxProposalsPerDay}).", "PROPOSAL_DAILY_LIMIT", TimeSpan.FromHours(1));

        var match = existing;
        if (match is null)
        {
            match = SearchRequestMatch.Create(request.Id, c.PropertyId, result.Score, result.Reasons, now);
            _repo.AddMatch(match);
        }
        else
        {
            match.Rescore(result.Score, result.Reasons, now);
        }

        match.MarkProposed(c.UserId, now);
        var alreadyTold = match.RequesterNotifiedAt is not null;
        match.MarkRequesterNotified(now);
        await _uow.SaveChangesAsync(ct);

        if (!alreadyTold)
        {
            var title = await _repo.GetListingTitleAsync(c.PropertyId, ct) ?? string.Empty;
            await _notifications.NotifySearchRequestMatchAsync(
                request.UserId, c.PropertyId, title, request.Id, proposed: true, ct);
        }

        return Unit.Value;
    }

    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        var v => v,
    };
}
