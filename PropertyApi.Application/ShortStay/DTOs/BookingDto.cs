namespace PropertyApi.Application.ShortStay.DTOs;

public sealed record BookingDto(
    Guid Id,
    Guid UnitId,
    Guid ShortStayListingId,
    string ListingTitle,
    Guid GuestId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Children,
    int Infants,
    string Mode,
    string PaymentMethod,
    decimal TotalAmount,
    decimal DepositAmount,
    decimal RemainingAmount,
    string Status,
    string? HostNote,
    string? CancellationReason,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    DateTime? CancelledAt);

public sealed record UnitAvailabilityRangeDto(DateOnly CheckIn, DateOnly CheckOut, string Status);
