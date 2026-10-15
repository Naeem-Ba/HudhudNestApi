using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Commands.CreateOffer;

public sealed class CreateOfferCommandHandler : IRequestHandler<CreateOfferCommand, Guid>
{
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public CreateOfferCommandHandler(IOfferRepository offers, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _offers = offers;
        _uow = uow;
        _auditLog = auditLog;
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

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.OfferCreatedByAdmin,
            request.IpAddress,
            oldValue: null,
            newValue: $"{{\"id\":\"{offer.Id}\",\"status\":\"{offer.Status}\"}}",
            cancellationToken);

        return offer.Id;
    }
}
