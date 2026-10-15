using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;

namespace HudhudNestApi.Application.Marketing.Queries.GetOffers;

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
        var nowUtc = DateTime.UtcNow;

        return offers
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => OfferAdminMapper.ToDto(o, nowUtc))
            .ToList();
    }
}
