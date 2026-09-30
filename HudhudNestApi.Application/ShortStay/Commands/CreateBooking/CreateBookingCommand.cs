using MediatR;
using HudhudNestApi.Application.ShortStay.DTOs;

namespace HudhudNestApi.Application.ShortStay.Commands.CreateBooking;

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
