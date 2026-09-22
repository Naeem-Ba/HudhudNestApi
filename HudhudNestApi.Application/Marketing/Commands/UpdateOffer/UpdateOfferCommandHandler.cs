using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;

namespace HudhudNestApi.Application.Marketing.Commands.UpdateOffer;

public sealed class UpdateOfferCommandHandler : IRequestHandler<UpdateOfferCommand, bool>
{
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;

    public UpdateOfferCommandHandler(IOfferRepository offers, IUnitOfWork uow)
    {
        _offers = offers;
        _uow = uow;
    }

    public async Task<bool> Handle(UpdateOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await _offers.GetByIdAsync(request.Id, cancellationToken);
        if (offer is null)
            return false;

        offer.UpdateDetails(
            request.Name,
            request.DiscountType,
            request.DiscountValue,
            request.StartsAtUtc,
            request.Description,
            request.TargetPlanTier,
            request.EndsAtUtc,
            request.MaxRedemptions,
            request.Terms);

        await _uow.SaveChangesAsync(cancellationToken);
        return true;
    }
}
