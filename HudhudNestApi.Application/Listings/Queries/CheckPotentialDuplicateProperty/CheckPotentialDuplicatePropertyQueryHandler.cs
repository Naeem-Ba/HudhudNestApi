using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Queries.CheckPotentialDuplicateProperty;

public sealed class CheckPotentialDuplicatePropertyQueryHandler
    : IRequestHandler<CheckPotentialDuplicatePropertyQuery, DuplicateCheckResultDto>
{
    // Same owner reposting the same unit is common and expected — allow a wider
    // price/area drift before flagging it (rent negotiated up/down, a re-measured area, etc).
    private const decimal SameOwnerTolerancePercent = 10m;

    // A different owner listing something that looks nearly identical is a much
    // stronger signal (possible unauthorized repost / scraped listing / fraud) —
    // use a tighter window so we don't flood owners with false positives.
    private const decimal DifferentOwnerTolerancePercent = 5m;

    private readonly IPropertyRepository _repo;

    public CheckPotentialDuplicatePropertyQueryHandler(IPropertyRepository repo)
    {
        _repo = repo;
    }

    public async Task<DuplicateCheckResultDto> Handle(
        CheckPotentialDuplicatePropertyQuery request,
        CancellationToken cancellationToken)
    {
        // Can't compare listings meaningfully without a structured location.
        if (!request.NeighborhoodId.HasValue)
            return new DuplicateCheckResultDto { HasPotentialDuplicates = false };

        // Fetch the widest candidate pool (the same-owner tolerance, which is the
        // larger of the two) — different-owner candidates get filtered down to the
        // tighter tolerance in-memory below.
        var candidates = await _repo.FindPotentialDuplicatesAsync(
            neighborhoodId: request.NeighborhoodId.Value,
            listingType: request.ListingType,
            price: request.Price,
            area: request.Area,
            maxTolerancePercent: SameOwnerTolerancePercent,
            ct: cancellationToken);

        var result = new DuplicateCheckResultDto();

        foreach (var candidate in candidates)
        {
            var isSameOwner = candidate.OwnerId == request.OwnerId;
            var tolerance = isSameOwner ? SameOwnerTolerancePercent : DifferentOwnerTolerancePercent;

            if (!IsWithinTolerance(candidate, request, tolerance))
                continue;

            result.Candidates.Add(new DuplicateCandidateDto
            {
                PropertyId = candidate.Id,
                Title = candidate.Title,
                IsSameOwner = isSameOwner,
                MatchReason = BuildMatchReason(isSameOwner, tolerance)
            });
        }

        result.HasPotentialDuplicates = result.Candidates.Count > 0;
        return result;
    }

    private static bool IsWithinTolerance(
        Property candidate,
        CheckPotentialDuplicatePropertyQuery request,
        decimal tolerancePercent)
    {
        var priceMatches = !request.Price.HasValue || request.Price.Value <= 0
            || IsWithinPercent(GetComparablePrice(candidate, request.ListingType), request.Price.Value, tolerancePercent);

        var areaMatches = !request.Area.HasValue || request.Area.Value <= 0
            || IsWithinPercent(candidate.Area, request.Area.Value, tolerancePercent);

        return priceMatches && areaMatches;
    }

    private static decimal? GetComparablePrice(Property property, ListingType listingType) =>
        listingType switch
        {
            ListingType.ForSale => property.PurchasePrice,
            _ => property.ColdRent ?? property.WarmRent
        };

    private static bool IsWithinPercent(decimal? candidateValue, decimal requestedValue, decimal tolerancePercent)
    {
        if (!candidateValue.HasValue || candidateValue.Value <= 0)
            return false;

        var diffPercent = Math.Abs(candidateValue.Value - requestedValue) / requestedValue * 100m;
        return diffPercent <= tolerancePercent;
    }

    private static string BuildMatchReason(bool isSameOwner, decimal tolerancePercent) => isSameOwner
        ? $"Same owner, same neighborhood and listing type, price/area within {tolerancePercent}% — likely a repost."
        : $"Different owner, same neighborhood and listing type, price/area within {tolerancePercent}% — possible duplicate listing.";
}
