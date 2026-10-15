using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.SetOfferStatus;

public sealed class SetOfferStatusCommandHandler : IRequestHandler<SetOfferStatusCommand, OfferAdminDto?>
{
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public SetOfferStatusCommandHandler(IOfferRepository offers, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _offers = offers;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task<OfferAdminDto?> Handle(SetOfferStatusCommand request, CancellationToken cancellationToken)
    {
        var offer = await _offers.GetByIdAsync(request.Id, cancellationToken);
        if (offer is null)
            return null;

        var nowUtc = DateTime.UtcNow;
        var previous = offer.Status;

        switch (request.Status)
        {
            case OfferStatus.Active:
                offer.ActivateAt(nowUtc);
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

        if (offer.Status != previous)
        {
            await _uow.SaveChangesAsync(cancellationToken);

            await _auditLog.LogAsync(
                request.PerformedByUserId,
                AuditActions.OfferStatusChangedByAdmin,
                request.IpAddress,
                oldValue: $"{{\"id\":\"{offer.Id}\",\"status\":\"{previous}\"}}",
                newValue: $"{{\"id\":\"{offer.Id}\",\"status\":\"{offer.Status}\"}}",
                cancellationToken);
        }

        return OfferAdminMapper.ToDto(offer, nowUtc);
    }
}
