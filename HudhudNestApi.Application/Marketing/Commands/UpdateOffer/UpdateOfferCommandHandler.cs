using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.Marketing.Commands.UpdateOffer;

public sealed class UpdateOfferCommandHandler : IRequestHandler<UpdateOfferCommand, bool>
{
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public UpdateOfferCommandHandler(IOfferRepository offers, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _offers = offers;
        _uow = uow;
        _auditLog = auditLog;
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

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.OfferUpdatedByAdmin,
            request.IpAddress,
            oldValue: null,
            newValue: $"{{\"id\":\"{offer.Id}\",\"status\":\"{offer.Status}\"}}",
            cancellationToken);

        return true;
    }
}
