using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Marketing.Commands.SetOfferStatus;

public sealed class SetOfferStatusCommandHandler : IRequestHandler<SetOfferStatusCommand, bool>
{
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;

    public SetOfferStatusCommandHandler(IOfferRepository offers, IUnitOfWork uow)
    {
        _offers = offers;
        _uow = uow;
    }

    public async Task<bool> Handle(SetOfferStatusCommand request, CancellationToken cancellationToken)
    {
        var offer = await _offers.GetByIdAsync(request.Id, cancellationToken);
        if (offer is null)
            return false;

        switch (request.Status)
        {
            case OfferStatus.Active:
                offer.Activate();
                break;
            case OfferStatus.Paused:
                offer.Pause();
                break;
            case OfferStatus.Ended:
                offer.End();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Status,
                    "لا يمكن ضبط حالة العرض على 'مسودة' بعد إنشائه.");
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return true;
    }
}
