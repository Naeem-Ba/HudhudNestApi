using MediatR;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;

namespace PropertyApi.Application.ShortStay.Queries.GetMyShortStayBookings;

/// <summary>Bookings made BY this user as a guest.</summary>
public sealed record GetMyShortStayBookingsQuery(Guid GuestId) : IRequest<IReadOnlyList<BookingDto>>;

public sealed class GetMyShortStayBookingsQueryHandler
    : IRequestHandler<GetMyShortStayBookingsQuery, IReadOnlyList<BookingDto>>
{
    private readonly IBookingRepository _bookings;

    public GetMyShortStayBookingsQueryHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<IReadOnlyList<BookingDto>> Handle(GetMyShortStayBookingsQuery request, CancellationToken ct)
    {
        var bookings = await _bookings.GetByGuestIdAsync(request.GuestId, ct);

        return bookings
            .Select(b => b.ToDto(b.Unit!.RoomType.ShortStayListingId, b.Unit.RoomType.ShortStayListing.Title, b.Unit.RoomType.ShortStayListing.CurrencyCode))
            .ToList();
    }
}
