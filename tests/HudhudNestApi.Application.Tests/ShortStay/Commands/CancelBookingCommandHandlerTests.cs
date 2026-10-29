using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.ShortStay.Commands.CancelBooking;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Domain.ShortStay.ValueObjects;

namespace HudhudNestApi.Application.Tests.ShortStay.Commands;

/// <summary>
/// QA phase 6 (P6-D03): cancelling somebody else's booking used to surface the domain rule as a
/// 400 business-rule violation; the other booking commands answer a non-party with 403.
/// </summary>
public sealed class CancelBookingCommandHandlerTests
{
    [Fact]
    public async Task Cancel_ByNonGuest_IsForbidden_AndDoesNotSave()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var booking = Booking.Create(
            unitId: Guid.NewGuid(),
            guestId: Guid.NewGuid(),
            checkIn: today.AddDays(5),
            checkOut: today.AddDays(8),
            guests: GuestComposition.Create(2, 0, 0),
            mode: BookingMode.Request,
            paymentMethod: PaymentMethod.PayOnArrival,
            totalAmount: 300m,
            depositAmount: 0m,
            cancellationPolicyFreeCancellationDays: 2,
            cancellationPolicyDepositRefundable: false,
            cancellationPolicyCustomTermsText: null,
            houseRulesSnapshotText: "لا تدخين",
            houseRulesAccepted: true);

        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(x => x.GetByIdWithListingIncludingDeletedAsync(booking.Id, It.IsAny<CancellationToken>())).ReturnsAsync(booking);
        var uow = new Mock<IUnitOfWork>();

        var handler = new CancelBookingCommandHandler(bookings.Object, uow.Object, Mock.Of<INotificationService>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CancelBookingCommand(booking.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(BookingStatus.Pending, booking.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
