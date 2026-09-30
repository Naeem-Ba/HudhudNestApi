using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.ShortStay.Commands.DeleteShortStayListing;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.Tests.ShortStay.Commands;

/// <summary>
/// Covers the delete path Short-Stay had none of before this fix (no Domain method, no
/// Command, no endpoint) — see CreateShortStayListingCommandHandlerTests' file header for the
/// wider context. Mirrors DeletePropertyCommandHandler's own test shape.
/// </summary>
public sealed class DeleteShortStayListingCommandHandlerTests
{
    private static ShortStayListing CreateListing(Guid ownerId) => ShortStayListing.Create(
        ownerId, 1, "شاليه على البحر", "وصف كافٍ للإعلان", 4, 2, 1,
        new TimeOnly(14, 0), new TimeOnly(11, 0), 35.5m, 35.8m);

    private static DeleteShortStayListingCommandHandler MakeHandler(
        Mock<IShortStayListingRepository> listings,
        Mock<IUnitOfWork>? uow = null,
        Mock<IMediaStorageService>? storage = null,
        Mock<IAuditLogService>? auditLogs = null,
        Mock<IBookingRepository>? bookings = null)
        => new(
            listings.Object,
            (bookings ?? new Mock<IBookingRepository>()).Object,
            (storage ?? new Mock<IMediaStorageService>()).Object,
            (uow ?? new Mock<IUnitOfWork>()).Object,
            (auditLogs ?? new Mock<IAuditLogService>()).Object,
            NullLogger<DeleteShortStayListingCommandHandler>.Instance);

    [Fact]
    public async Task Handle_Owner_SoftDeletesAndRemovesFromRepository()
    {
        var ownerId = Guid.NewGuid();
        var listing = CreateListing(ownerId);

        var listings = new Mock<IShortStayListingRepository>();
        listings.Setup(x => x.GetByIdWithDetailsAsync(listing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listing);

        var handler = MakeHandler(listings);

        var result = await handler.Handle(
            new DeleteShortStayListingCommand(listing.Id, ownerId), CancellationToken.None);

        Assert.True(result);
        Assert.True(listing.IsDeleted);
        Assert.Equal(ownerId, listing.DeletedByUserId);
        listings.Verify(x => x.Remove(listing), Times.Once);
    }

    [Fact]
    public async Task Handle_ListingWithActiveBookings_IsAConflict_AndNothingIsRemoved()
    {
        var ownerId = Guid.NewGuid();
        var listing = CreateListing(ownerId);

        var listings = new Mock<IShortStayListingRepository>();
        listings.Setup(x => x.GetByIdWithDetailsAsync(listing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listing);
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(x => x.HasActiveBookingsForListingAsync(listing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var uow = new Mock<IUnitOfWork>();

        var handler = MakeHandler(listings, uow, bookings: bookings);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new DeleteShortStayListingCommand(listing.Id, ownerId), CancellationToken.None));

        Assert.False(listing.IsDeleted);
        listings.Verify(x => x.Remove(It.IsAny<ShortStayListing>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotTheOwner_IsForbidden_AndNothingIsRemoved()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var listing = CreateListing(ownerId);

        var listings = new Mock<IShortStayListingRepository>();
        listings.Setup(x => x.GetByIdWithDetailsAsync(listing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listing);

        var handler = MakeHandler(listings);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new DeleteShortStayListingCommand(listing.Id, otherUserId), CancellationToken.None));

        Assert.False(listing.IsDeleted);
        listings.Verify(x => x.Remove(It.IsAny<ShortStayListing>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownListing_ThrowsNotFound()
    {
        var listings = new Mock<IShortStayListingRepository>();
        listings.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShortStayListing?)null);

        var handler = MakeHandler(listings);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new DeleteShortStayListingCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
