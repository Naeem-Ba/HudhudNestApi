using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Commands.CreateOffer;

public sealed class CreateOfferCommandHandler : IRequestHandler<CreateOfferCommand, Guid>
{
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;

    public CreateOfferCommandHandler(IOfferRepository offers, IUnitOfWork uow)
    {
        _offers = offers;
        _uow = uow;
    }

    public async Task<Guid> Handle(CreateOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = Offer.Create(
            request.Name,
            request.DiscountType,
            request.DiscountValue,
            request.StartsAtUtc,
            request.Description,
            request.TargetPlanTier,
            request.EndsAtUtc,
            request.MaxRedemptions,
            request.Terms);

        _offers.Add(offer);
        await _uow.SaveChangesAsync(cancellationToken);

        return offer.Id;
    }
}
