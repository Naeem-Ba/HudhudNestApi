using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;
using PropertyApi.Domain.ShortStay.ValueObjects;

namespace PropertyApi.Application.ShortStay.Commands.CreateBooking;

/// <summary>
/// Creates a booking under a PostgreSQL advisory lock scoped to the unit (see
/// ShortStayBookingLock), re-validating no overlapping Reserved/CheckedIn range exists right
/// before inserting — the same "read-then-write race" pattern already closed for listing quota
/// (CreatePropertyCommandHandler). The DB-level EXCLUDE constraint on UnitBookingRange (added
/// in the AddShortStayAvailability migration) is the second, unconditional guarantee: even if
/// this check/lock were somehow bypassed, the INSERT itself would fail on an actual overlap.
/// </summary>
public sealed class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, BookingDto>
{
    private readonly IAccommodationUnitRepository _units;
    private readonly IBookingRepository _bookings;
    private readonly IPricingRuleRepository _pricingRules;
    private readonly IMinimumStayRuleRepository _minimumStayRules;
    private readonly IPricingCalculationService _pricing;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<CreateBookingCommandHandler> _logger;

    public CreateBookingCommandHandler(
        IAccommodationUnitRepository units,
        IBookingRepository bookings,
        IPricingRuleRepository pricingRules,
        IMinimumStayRuleRepository minimumStayRules,
        IPricingCalculationService pricing,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<CreateBookingCommandHandler> logger)
    {
        _units = units;
        _bookings = bookings;
        _pricingRules = pricingRules;
        _minimumStayRules = minimumStayRules;
        _pricing = pricing;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<BookingDto> Handle(CreateBookingCommand request, CancellationToken ct)
    {
        var unit = await _units.GetByIdWithListingAsync(request.UnitId, ct)
            ?? throw new NotFoundException($"Unit {request.UnitId} was not found.");

        var roomType = unit.RoomType;
        var listing = roomType.ShortStayListing;

        if (!listing.IsPublished)
            throw new DomainException("لا يمكن الحجز في إعلان غير منشور.");

        if (!unit.IsActive)
            throw new DomainException("هذه الوحدة غير متاحة للحجز حالياً.");

        if (listing.OwnerId == request.GuestId)
            throw new DomainException("لا يمكن للمالك حجز إعلانه الخاص.");

        if (!listing.InstantBookingEnabled && !listing.RequestBookingEnabled)
            throw new DomainException("هذا الإعلان لا يدعم أي وضع حجز حالياً.");

        var mode = listing.InstantBookingEnabled ? BookingMode.Instant : BookingMode.Request;

        var guests = GuestComposition.Create(request.Adults, request.Children, request.Infants);
        var capacity = roomType.Capacity ?? listing.Capacity;
        if (guests.CountedGuests > capacity)
            throw new DomainException($"عدد الضيوف يتجاوز السعة المسموحة ({capacity}) لهذه الوحدة.");

        var paymentMethod = Enum.Parse<PaymentMethod>(request.PaymentMethod, ignoreCase: true);
        if (paymentMethod == PaymentMethod.OnlinePayment)
            throw new DomainException("الدفع الإلكتروني غير مُفعَّل حالياً — اختر طريقة دفع أخرى.");

        var pricingRules = await _pricingRules.GetByRoomTypeIdAsync(roomType.Id, ct);
        var minimumStayRules = await _minimumStayRules.GetByRoomTypeIdAsync(roomType.Id, ct);

        var breakdown = _pricing.Calculate(
            roomType, pricingRules, minimumStayRules,
            listing.CleaningFee, listing.ExtraGuestFee, listing.ExtraBedFee,
            listing.Capacity, request.CheckIn, request.CheckOut, guests.CountedGuests);

        var depositAmount = listing.DepositPercentage.HasValue
            ? Math.Round(breakdown.TotalAmount * listing.DepositPercentage.Value / 100m, 2)
            : 0m;

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireAdvisoryLockAsync(ShortStayBookingLock.ForUnit(unit.Id), ct);

            var overlaps = await _bookings.HasOverlappingReservationAsync(
                unit.Id, request.CheckIn, request.CheckOut, ct);

            if (overlaps)
                throw new ConflictException("هذه التواريخ لم تعد متاحة لهذه الوحدة.");

            var booking = Booking.Create(
                unit.Id,
                request.GuestId,
                request.CheckIn,
                request.CheckOut,
                guests,
                mode,
                paymentMethod,
                breakdown.TotalAmount,
                depositAmount,
                listing.CancellationFreeCancellationDays,
                listing.CancellationDepositRefundable,
                listing.CancellationCustomTermsText,
                listing.BuildHouseRulesSnapshotText(),
                request.HouseRulesAccepted);

            await _bookings.AddAsync(booking, ct);

            var rangeStatus = booking.Status is BookingStatus.Confirmed or BookingStatus.Approved
                or BookingStatus.DepositPaid
                ? UnitRangeStatus.Reserved
                : UnitRangeStatus.Pending;

            await _bookings.AddBookingRangeAsync(new UnitBookingRange
            {
                UnitId = unit.Id,
                CheckIn = request.CheckIn,
                CheckOut = request.CheckOut,
                Status = rangeStatus,
                BookingId = booking.Id,
            }, ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            try
            {
                await _notifications.NotifyShortStayBookingUpdateAsync(
                    recipientId: listing.OwnerId,
                    bookingId: booking.Id,
                    listingTitle: listing.Title,
                    type: NotificationType.ShortStayBookingRequested,
                    detail: $"طلب حجز جديد لـ '{listing.Title}' من {request.CheckIn:dd/MM/yyyy} إلى {request.CheckOut:dd/MM/yyyy}.",
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send booking-created notification. BookingId={BookingId}, ListingId={ListingId}",
                    booking.Id, listing.Id);
            }

            return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }
    }
}
