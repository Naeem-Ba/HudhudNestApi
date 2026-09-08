using MediatR;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Queries.GetOffers;

public sealed class GetOffersQueryHandler
    : IRequestHandler<GetOffersQuery, IReadOnlyList<OfferAdminDto>>
{
    private readonly IOfferRepository _offers;

    public GetOffersQueryHandler(IOfferRepository offers)
        => _offers = offers;

    public async Task<IReadOnlyList<OfferAdminDto>> Handle(
        GetOffersQuery request,
        CancellationToken cancellationToken)
    {
        var offers = await _offers.GetAllAsync(cancellationToken);

        return offers
            .OrderByDescending(o => o.CreatedAt)
            .Select(ToDto)
            .ToList();
    }

    private static OfferAdminDto ToDto(Offer offer) => new(
        offer.Id,
        offer.Name,
        offer.Description,
        offer.DiscountType.ToString(),
        offer.DiscountValue,
        offer.TargetPlanTier,
        offer.StartsAtUtc,
        offer.EndsAtUtc,
        offer.MaxRedemptions,
        offer.RedeemedCount,
        offer.Status.ToString(),
        offer.Terms,
        offer.CreatedAt,
        offer.UpdatedAt);
}
