using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Enums;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.ValueObjects;

namespace PropertyApi.Application.Tests.ShortStay.Domain;

public sealed class BookingStateMachineTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static Booking CreatePending(BookingMode mode = BookingMode.Request, decimal deposit = 0m) =>
        Booking.Create(
            unitId: Guid.NewGuid(),
            guestId: Guid.NewGuid(),
            checkIn: Today.AddDays(5),
            checkOut: Today.AddDays(8),
            guests: GuestComposition.Create(2, 0, 0),
            mode: mode,
            paymentMethod: PaymentMethod.PayOnArrival,
            totalAmount: 300m,
            depositAmount: deposit,
            cancellationPolicyFreeCancellationDays: 2,
            cancellationPolicyDepositRefundable: false,
            cancellationPolicyCustomTermsText: null,
            houseRulesSnapshotText: "لا تدخين",
            houseRulesAccepted: true);

    [Fact]
    public void Create_RequestMode_StartsAtPending()
    {
        var booking = CreatePending();
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public void Create_InstantMode_NoDeposit_StartsAtConfirmed()
    {
        var booking = CreatePending(BookingMode.Instant, deposit: 0m);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void Create_InstantMode_WithDeposit_StartsAtApproved()
    {
        var booking = CreatePending(BookingMode.Instant, deposit: 50m);
        Assert.Equal(BookingStatus.Approved, booking.Status);
    }

    [Fact]
    public void Create_Throws_WhenCheckOutNotAfterCheckIn()
    {
        Assert.Throws<DomainException>(() => Booking.Create(
            Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(5), Today.AddDays(5),
            GuestComposition.Create(1, 0, 0), BookingMode.Request, PaymentMethod.PayOnArrival,
            100m, 0m, 1, false, null, "rules", true));
    }

    [Fact]
    public void Create_Throws_WhenHouseRulesNotAccepted()
    {
        Assert.Throws<DomainException>(() => Booking.Create(
            Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(5), Today.AddDays(8),
            GuestComposition.Create(1, 0, 0), BookingMode.Request, PaymentMethod.PayOnArrival,
            100m, 0m, 1, false, null, "rules", houseRulesAccepted: false));
    }

    [Fact]
    public void Create_Throws_WhenDepositExceedsTotal()
    {
        Assert.Throws<DomainException>(() => Booking.Create(
            Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(5), Today.AddDays(8),
            GuestComposition.Create(1, 0, 0), BookingMode.Request, PaymentMethod.PayOnArrival,
            100m, 150m, 1, false, null, "rules", true));
    }

    [Fact]
    public void FullHappyPath_RequestBooking_WithDeposit_ReachesCompleted()
    {
        var booking = CreatePending(BookingMode.Request, deposit: 50m);

        booking.Approve("مرحباً بك");
        Assert.Equal(BookingStatus.Approved, booking.Status);

        booking.RecordDepositPaid();
        Assert.Equal(BookingStatus.DepositPaid, booking.Status);

        booking.Confirm();
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        booking.CheckInGuest();
        Assert.Equal(BookingStatus.CheckedIn, booking.Status);

        booking.CheckOutGuest();
        Assert.Equal(BookingStatus.CheckedOut, booking.Status);

        booking.Complete();
        Assert.Equal(BookingStatus.Completed, booking.Status);
    }

    [Fact]
    public void Reject_FromPending_Succeeds()
    {
        var booking = CreatePending();
        booking.Reject("لا يتوفر مكان");
        Assert.Equal(BookingStatus.Rejected, booking.Status);
    }

    [Fact]
    public void Reject_FromApproved_Succeeds()
    {
        var booking = CreatePending();
        booking.Approve();
        booking.Reject("تراجع المضيف");
        Assert.Equal(BookingStatus.Rejected, booking.Status);
    }

    [Fact]
    public void Reject_FromConfirmed_Throws()
    {
        var booking = CreatePending(BookingMode.Instant);
        Assert.Throws<InvalidStateTransitionException>(() => booking.Reject());
    }

    [Fact]
    public void Confirm_WithoutApprovalFirst_Throws()
    {
        var booking = CreatePending();
        Assert.Throws<InvalidStateTransitionException>(() => booking.Confirm());
    }

    [Fact]
    public void Complete_SkippingCheckInCheckOut_Throws()
    {
        var booking = CreatePending(BookingMode.Instant);
        Assert.Throws<InvalidStateTransitionException>(() => booking.Complete());
    }

    [Fact]
    public void Cancel_ByGuest_FromPending_Succeeds()
    {
        var booking = CreatePending();
        booking.Cancel(booking.GuestId, "غيّرت خططي");
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void Cancel_ByNonGuest_Throws()
    {
        var booking = CreatePending();
        Assert.Throws<DomainException>(() => booking.Cancel(Guid.NewGuid()));
    }

    [Fact]
    public void Cancel_AfterCompleted_Throws()
    {
        var booking = CreatePending(BookingMode.Instant);
        booking.CheckInGuest();
        booking.CheckOutGuest();
        booking.Complete();

        Assert.Throws<InvalidStateTransitionException>(() => booking.Cancel(booking.GuestId));
    }

    [Fact]
    public void MarkExpired_FromPending_Succeeds()
    {
        var booking = CreatePending();
        booking.MarkExpired();
        Assert.Equal(BookingStatus.Expired, booking.Status);
    }

    [Fact]
    public void MarkExpired_FromConfirmed_Throws()
    {
        var booking = CreatePending(BookingMode.Instant);
        Assert.Throws<InvalidStateTransitionException>(() => booking.MarkExpired());
    }

    [Fact]
    public void MarkNoShow_FromConfirmed_Succeeds()
    {
        var booking = CreatePending(BookingMode.Instant);
        booking.MarkNoShow();
        Assert.Equal(BookingStatus.NoShow, booking.Status);
    }

    [Fact]
    public void MarkNoShow_FromPending_Throws()
    {
        var booking = CreatePending();
        Assert.Throws<InvalidStateTransitionException>(() => booking.MarkNoShow());
    }
}
