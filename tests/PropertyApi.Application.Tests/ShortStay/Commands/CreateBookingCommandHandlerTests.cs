using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.ShortStay.Commands.CreateBooking;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Services;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.Tests.ShortStay.Commands;

public sealed class CreateBookingCommandHandlerTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static (AccommodationUnit unit, ShortStayListing listing) CreatePublishedUnit(
        Guid ownerId, bool instantBooking = true, int capacity = 4, decimal basePrice = 100m)
    {
        var listing = ShortStayListing.Create(
            ownerId, 1, "Test Listing", "desc", capacity, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m);
        listing.UpdateBookingSettings(instantBooking, !instantBooking);

        var roomType = new RoomType { Name = "Default", BasePricePerNight = basePrice, ShortStayListing = listing };
        var unit = new AccommodationUnit { Label = "Unit 1", RoomType = roomType };
        roomType.Units.Add(unit);
        listing.RoomTypes.Add(roomType);
        listing.Publish();

        return (unit, listing);
    }

    private static CreateBookingCommandHandler MakeHandler(
        AccommodationUnit unit,
        bool hasOverlap = false,
        IReadOnlyList<PricingRule>? pricingRules = null,
        IReadOnlyList<MinimumStayRule>? minimumStayRules = null)
    {
        return new CreateBookingCommandHandler(
            new StubUnitRepository(unit),
            new StubBookingRepository(hasOverlap),
            new StubPricingRuleRepository(pricingRules ?? []),
            new StubMinimumStayRuleRepository(minimumStayRules ?? []),
            new PricingCalculationService(),
            new NoOpUnitOfWork(),
            new NoOpNotificationService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateBookingCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidInstantBooking_ReturnsConfirmedBooking()
    {
        var owner = Guid.NewGuid();
        var (unit, _) = CreatePublishedUnit(owner);
        var handler = MakeHandler(unit);

        var result = await handler.Handle(
            new CreateBookingCommand(unit.Id, Guid.NewGuid(), Today.AddDays(5), Today.AddDays(7),
                2, 0, 0, "PayOnArrival", true),
            CancellationToken.None);

        Assert.Equal("Confirmed", result.Status);
        Assert.Equal(200m, result.TotalAmount);
    }

    [Fact]
    public async Task Handle_Throws_WhenListingNotPublished()
    {
        var owner = Guid.NewGuid();
        var listing = ShortStayListing.Create(owner, 1, "T", "d", 4, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m);
        listing.UpdateBookingSettings(true, false);
        var roomType = new RoomType { Name = "Default", BasePricePerNight = 100m, ShortStayListing = listing };
        var unit = new AccommodationUnit { Label = "Unit 1", RoomType = roomType };
        roomType.Units.Add(unit);
        listing.RoomTypes.Add(roomType);
        // deliberately NOT published

        var handler = MakeHandler(unit);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateBookingCommand(unit.Id, Guid.NewGuid(), Today.AddDays(5), Today.AddDays(7),
                2, 0, 0, "PayOnArrival", true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenOwnerBooksOwnListing()
    {
        var owner = Guid.NewGuid();
        var (unit, _) = CreatePublishedUnit(owner);
        var handler = MakeHandler(unit);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateBookingCommand(unit.Id, owner, Today.AddDays(5), Today.AddDays(7),
                2, 0, 0, "PayOnArrival", true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenGuestsExceedCapacity()
    {
        var owner = Guid.NewGuid();
        var (unit, _) = CreatePublishedUnit(owner, capacity: 2);
        var handler = MakeHandler(unit);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateBookingCommand(unit.Id, Guid.NewGuid(), Today.AddDays(5), Today.AddDays(7),
                5, 0, 0, "PayOnArrival", true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenOnlinePaymentRequested()
    {
        var owner = Guid.NewGuid();
        var (unit, _) = CreatePublishedUnit(owner);
        var handler = MakeHandler(unit);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateBookingCommand(unit.Id, Guid.NewGuid(), Today.AddDays(5), Today.AddDays(7),
                2, 0, 0, "OnlinePayment", true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_ConflictException_WhenDatesNoLongerAvailable()
    {
        // Simulates the race this handler exists to close: HasOverlappingReservationAsync
        // (checked inside the advisory-lock-guarded transaction) says the dates are taken.
        var owner = Guid.NewGuid();
        var (unit, _) = CreatePublishedUnit(owner);
        var handler = MakeHandler(unit, hasOverlap: true);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateBookingCommand(unit.Id, Guid.NewGuid(), Today.AddDays(5), Today.AddDays(7),
                2, 0, 0, "PayOnArrival", true),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RequestMode_StartsAtPending_NotConfirmed()
    {
        var owner = Guid.NewGuid();
        var (unit, _) = CreatePublishedUnit(owner, instantBooking: false);
        var handler = MakeHandler(unit);

        var result = await handler.Handle(
            new CreateBookingCommand(unit.Id, Guid.NewGuid(), Today.AddDays(5), Today.AddDays(7),
                2, 0, 0, "PayOnArrival", true),
            CancellationToken.None);

        Assert.Equal("Pending", result.Status);
    }

    // ── Stubs ───────────────────────────────────────────────────────

    private sealed class StubUnitRepository : IAccommodationUnitRepository
    {
        private readonly AccommodationUnit _unit;
        public StubUnitRepository(AccommodationUnit unit) => _unit = unit;

        public Task AddAsync(AccommodationUnit unit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AccommodationUnit?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<AccommodationUnit?>(id == _unit.Id ? _unit : null);
        public Task<AccommodationUnit?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<AccommodationUnit?>(id == _unit.Id ? _unit : null);
    }

    private sealed class StubBookingRepository : IBookingRepository
    {
        private readonly bool _hasOverlap;
        public StubBookingRepository(bool hasOverlap) => _hasOverlap = hasOverlap;

        public Task AddAsync(Booking booking, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Booking?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> HasOverlappingReservationAsync(Guid unitId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct = default)
            => Task.FromResult(_hasOverlap);
        public Task AddBookingRangeAsync(UnitBookingRange range, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkRangeReservedAsync(Guid bookingId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkRangeCheckedInAsync(Guid bookingId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseRangeAsync(Guid bookingId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Booking>> GetOverlappingPendingBookingsAsync(Guid unitId, DateOnly checkIn, DateOnly checkOut, Guid excludingBookingId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Booking>>([]);
        public Task<IReadOnlyList<Booking>> GetByGuestIdAsync(Guid guestId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Booking>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> HasCompletedBookingAsync(Guid bookingId, Guid guestId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> HasReviewAsync(Guid bookingId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<UnitBookingRange>> GetRangesForUnitAsync(Guid unitId, DateOnly from, DateOnly to, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubPricingRuleRepository : IPricingRuleRepository
    {
        private readonly IReadOnlyList<PricingRule> _rules;
        public StubPricingRuleRepository(IReadOnlyList<PricingRule> rules) => _rules = rules;
        public Task<IReadOnlyList<PricingRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default) => Task.FromResult(_rules);
        public Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<PricingRule> rules, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubMinimumStayRuleRepository : IMinimumStayRuleRepository
    {
        private readonly IReadOnlyList<MinimumStayRule> _rules;
        public StubMinimumStayRuleRepository(IReadOnlyList<MinimumStayRule> rules) => _rules = rules;
        public Task<IReadOnlyList<MinimumStayRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default) => Task.FromResult(_rules);
        public Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<MinimumStayRule> rules, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task NotifyNewMessageAsync(Guid recipientId, Guid senderId, string senderName, Guid messageId, Guid propertyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyPropertyUpdateAsync(Guid recipientId, Guid propertyId, string propertyTitle, NotificationType type, string detail, Guid? relatedEntityId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySavedSearchMatchAsync(Guid recipientId, Guid propertyId, string propertyTitle, string savedSearchName, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyUserRatedAsync(Guid recipientId, Guid raterId, string raterName, double overallScore, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyListingExpiringSoonAsync(Guid recipientId, Guid propertyId, string propertyTitle, int daysRemaining, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyListingExpiredAsync(Guid recipientId, Guid propertyId, string propertyTitle, int graceDaysRemaining, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyAgencyInvitationReceivedAsync(Guid recipientId, Guid invitationId, string agencyName, string inviterName, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyAgencyInvitationRespondedAsync(Guid recipientId, Guid invitationId, string targetUserName, bool accepted, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyShortStayBookingUpdateAsync(Guid recipientId, Guid bookingId, string listingTitle, NotificationType type, string detail, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyValuationInquiryExpiredAsync(Guid recipientId, Guid inquiryId, bool hadPreliminaryEstimate, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyValuationOfficeInvitationExpiredAsync(Guid recipientId, Guid invitationId, Guid inquiryId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(Guid userId, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> SoftDeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
