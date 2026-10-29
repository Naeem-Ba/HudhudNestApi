using MediatR;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;

namespace HudhudNestApi.Application.ShortStay.Queries.GetMyShortStayBookings;

/// <summary>Bookings made BY this user as a guest.</summary>
public sealed record GetMyShortStayBookingsQuery(Guid GuestId) : IRequest<IReadOnlyList<BookingDto>>;

public sealed class GetMyShortStayBookingsQueryHandler
    : IRequestHandler<GetMyShortStayBookingsQuery, IReadOnlyList<BookingDto>>
{
    private readonly IBookingRepository _bookings;
    private readonly IShortStayReviewRepository _reviews;

    public GetMyShortStayBookingsQueryHandler(IBookingRepository bookings, IShortStayReviewRepository reviews)
    {
        _bookings = bookings;
        _reviews = reviews;
    }

    public async Task<IReadOnlyList<BookingDto>> Handle(GetMyShortStayBookingsQuery request, CancellationToken ct)
    {
        var bookings = await _bookings.GetByGuestIdAsync(request.GuestId, ct);

        // Only finished stays can be reviewed, so only those are looked up.
        var reviewed = await _reviews.GetReviewedBookingIdsAsync(
            bookings.Where(b => b.Status == Domain.ShortStay.Enums.BookingStatus.Completed).Select(b => b.Id).ToList(), ct);

        return bookings
            .Select(b => b.ToDto(b.Unit!.RoomType.ShortStayListingId, b.Unit.RoomType.ShortStayListing.Title, b.Unit.RoomType.ShortStayListing.CurrencyCode)
                with
            { Reviewed = reviewed.Contains(b.Id) })
            .ToList();
    }
}
