namespace HudhudNestApi.Application.ShortStay.DTOs;

public sealed record BookingDto(
    Guid Id,
    Guid UnitId,
    Guid ShortStayListingId,
    string ListingTitle,
    string CurrencyCode,
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
    DateTime? CancelledAt,
    // When an unconfirmed booking lapses (null once confirmed or finished); the app shows the countdown.
    DateTime? ConfirmationDeadline = null,
    // Set only where the caller is the guest: true once a Completed stay has been reviewed.
    bool? Reviewed = null);

public sealed record UnitAvailabilityRangeDto(DateOnly CheckIn, DateOnly CheckOut, string Status);
