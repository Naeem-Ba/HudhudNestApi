using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;

namespace HudhudNestApi.Application.ShortStay.Commands.RecordBookingDepositPaid;

/// <summary>
/// Manual settlement, mirroring the existing "quote now, confirm later, no gateway" pattern
/// used for featured-listing/extension payments (PropertiesController's admin-confirm
/// endpoints) — the deposit is recorded by the host once received out-of-band (bank transfer,
/// cash, etc.), matching PaymentMethod's current PayOnArrival/PhoneConfirmation/
/// ChatConfirmation options.
/// </summary>
public sealed record RecordBookingDepositPaidCommand(Guid BookingId, Guid HostId) : IRequest<BookingDto>;

public sealed class RecordBookingDepositPaidCommandHandler : IRequestHandler<RecordBookingDepositPaidCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;

    public RecordBookingDepositPaidCommandHandler(IBookingRepository bookings, IUnitOfWork uow)
    {
        _bookings = bookings;
        _uow = uow;
    }

    public async Task<BookingDto> Handle(RecordBookingDepositPaidCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        if (listing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can record a deposit payment.");

        booking.RecordDepositPaid();
        await _uow.SaveChangesAsync(ct);

        return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
    }
}
