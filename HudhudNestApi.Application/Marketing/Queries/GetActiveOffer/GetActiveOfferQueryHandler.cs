using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Queries.GetActiveOffer;

public sealed class GetActiveOfferQueryHandler
    : IRequestHandler<GetActiveOfferQuery, OfferDto?>
{
    private readonly IOfferRepository _offers;

    public GetActiveOfferQueryHandler(IOfferRepository offers)
        => _offers = offers;

    public async Task<OfferDto?> Handle(
        GetActiveOfferQuery request,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var offer = await _offers.GetCurrentActiveAsync(nowUtc, cancellationToken);

        // GetCurrentActiveAsync already filtered on Status/date at the database level, but
        // the redemption-count condition can only be checked once the row is loaded — an
        // offer that reached MaxRedemptions a moment ago must disappear from the public
        // surface immediately, not just once an admin manually ends it.
        if (offer is null || !offer.IsCurrentlyRedeemable(nowUtc))
            return null;

        return ToDto(offer);
    }

    private static OfferDto ToDto(Offer offer) => new(
        offer.Id,
        offer.Name,
        offer.Description,
        offer.DiscountType.ToString(),
        offer.DiscountValue,
        offer.TargetPlanTier,
        offer.EndsAtUtc,
        offer.MaxRedemptions,
        offer.RedeemedCount,
        offer.Terms);
}
