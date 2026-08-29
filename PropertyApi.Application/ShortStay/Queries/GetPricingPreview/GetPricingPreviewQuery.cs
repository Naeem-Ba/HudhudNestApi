using MediatR;
using PropertyApi.Application.ShortStay.DTOs;

namespace PropertyApi.Application.ShortStay.Queries.GetPricingPreview;

/// <summary>
/// Read-only pricing preview for a candidate stay, exposed so the booking UI can show the
/// guest a full breakdown before they submit CreateBookingCommand — the same
/// IPricingCalculationService call CreateBookingCommandHandler makes internally, just without
/// creating anything. This does not replace server-side authority over the final price: the
/// booking is always re-priced from scratch inside CreateBookingCommandHandler at submit time
/// (spec §18 — the frontend must never be trusted to compute or carry forward the amount).
/// </summary>
public sealed record GetPricingPreviewQuery(
    Guid UnitId, DateOnly CheckIn, DateOnly CheckOut, int Adults, int Children, int Infants)
    : IRequest<PricingBreakdownDto>;
