using MediatR;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;

namespace HudhudNestApi.Application.ShortStay.Queries.GetUnitAvailability;

public sealed record GetUnitAvailabilityQuery(Guid UnitId, DateOnly From, DateOnly To)
    : IRequest<IReadOnlyList<UnitAvailabilityRangeDto>>;

public sealed class GetUnitAvailabilityQueryHandler
    : IRequestHandler<GetUnitAvailabilityQuery, IReadOnlyList<UnitAvailabilityRangeDto>>
{
    private readonly IBookingRepository _bookings;

    public GetUnitAvailabilityQueryHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<IReadOnlyList<UnitAvailabilityRangeDto>> Handle(
        GetUnitAvailabilityQuery request, CancellationToken ct)
    {
        var ranges = await _bookings.GetRangesForUnitAsync(request.UnitId, request.From, request.To, ct);

        return ranges
            .Select(r => new UnitAvailabilityRangeDto(r.CheckIn, r.CheckOut, r.Status.ToString()))
            .ToList();
    }
}
