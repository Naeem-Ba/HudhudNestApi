using MediatR;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;

namespace HudhudNestApi.Application.ShortStay.Queries.GetHostBookingRequests;

/// <summary>Bookings made AGAINST every listing owned by this host, across all their units.</summary>
public sealed record GetHostBookingRequestsQuery(Guid HostId) : IRequest<IReadOnlyList<BookingDto>>;

public sealed class GetHostBookingRequestsQueryHandler
    : IRequestHandler<GetHostBookingRequestsQuery, IReadOnlyList<BookingDto>>
{
    private readonly IBookingRepository _bookings;

    public GetHostBookingRequestsQueryHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<IReadOnlyList<BookingDto>> Handle(GetHostBookingRequestsQuery request, CancellationToken ct)
    {
        var bookings = await _bookings.GetByHostIdAsync(request.HostId, ct);

        return bookings
            .Select(b => b.ToDto(b.Unit!.RoomType.ShortStayListingId, b.Unit.RoomType.ShortStayListing.Title, b.Unit.RoomType.ShortStayListing.CurrencyCode))
            .ToList();
    }
}
