using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Mapping;

public static class BookingMappingExtensions
{
    public static BookingDto ToDto(this Booking booking, Guid shortStayListingId, string listingTitle, string currencyCode) => new(
        Id: booking.Id,
        UnitId: booking.UnitId,
        ShortStayListingId: shortStayListingId,
        ListingTitle: listingTitle,
        CurrencyCode: currencyCode,
        GuestId: booking.GuestId,
        CheckIn: booking.CheckIn,
        CheckOut: booking.CheckOut,
        Adults: booking.Guests.Adults,
        Children: booking.Guests.Children,
        Infants: booking.Guests.Infants,
        Mode: booking.Mode.ToString(),
        PaymentMethod: booking.PaymentMethod.ToString(),
        TotalAmount: booking.TotalAmount,
        DepositAmount: booking.DepositAmount,
        RemainingAmount: booking.RemainingAmount,
        Status: booking.Status.ToString(),
        HostNote: booking.HostNote,
        CancellationReason: booking.CancellationReason,
        CreatedAt: booking.CreatedAt,
        RespondedAt: booking.RespondedAt,
        CancelledAt: booking.CancelledAt);
}
