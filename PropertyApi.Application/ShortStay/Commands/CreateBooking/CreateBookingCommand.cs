using MediatR;
using PropertyApi.Application.ShortStay.DTOs;

namespace PropertyApi.Application.ShortStay.Commands.CreateBooking;

public sealed record CreateBookingCommand(
    Guid UnitId,
    Guid GuestId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Children,
    int Infants,
    string PaymentMethod,
    bool HouseRulesAccepted) : IRequest<BookingDto>;
