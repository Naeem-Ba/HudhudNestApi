using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Commands.SubmitLead;

public sealed class SubmitLeadCommandHandler
    : IRequestHandler<SubmitLeadCommand, SubmitLeadResultDto>
{
    private readonly ILeadRepository _leads;
    private readonly IOfferRepository _offers;
    private readonly IUnitOfWork _uow;

    public SubmitLeadCommandHandler(
        ILeadRepository leads,
        IOfferRepository offers,
        IUnitOfWork uow)
    {
        _leads = leads;
        _offers = offers;
        _uow = uow;
    }

    public async Task<SubmitLeadResultDto> Handle(
        SubmitLeadCommand request,
        CancellationToken cancellationToken)
    {
        // Reserve the offer slot BEFORE creating the Lead: TryReserveRedemptionAsync is its
        // own atomic UPDATE, independent of this handler's SaveChanges, so we must know the
        // outcome before deciding whether the new Lead carries an OfferId at all. A lead is
        // never rejected because the offer ran out — only the OfferId link is (or isn't) set.
        //
        // Failure-mode note: if SaveChangesAsync below throws after a successful reservation,
        // the reservation is NOT rolled back (it already committed as its own statement) —
        // the offer's counter permanently loses that one slot with no Lead to show for it.
        // This fails safe (undercounts remaining slots, never overcounts/oversells) and is
        // an acceptable trade-off for a marketing capture path; it is not used anywhere a
        // real financial commitment is made.
        var offerApplied = false;
        if (request.OfferId.HasValue)
        {
            offerApplied = await _offers.TryReserveRedemptionAsync(
                request.OfferId.Value,
                cancellationToken);
        }

        var lead = Lead.Create(
            request.FullName,
            request.Phone,
            request.City,
            request.UserType,
            request.Source,
            request.Notes,
            request.Campaign,
            offerApplied ? request.OfferId : null,
            request.IpAddress);

        _leads.Add(lead);
        await _uow.SaveChangesAsync(cancellationToken);

        return new SubmitLeadResultDto(
            lead.Id,
            OfferRequested: request.OfferId.HasValue,
            OfferApplied: offerApplied);
    }
}
