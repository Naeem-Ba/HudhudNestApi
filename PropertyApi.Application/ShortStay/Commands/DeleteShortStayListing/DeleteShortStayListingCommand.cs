using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.ShortStay.Commands.DeleteShortStayListing;

/// <summary>
/// Soft-deletes a Short-Stay listing — mirrors DeletePropertyCommand's shape exactly
/// (ownership check, MarkAsDeleted + Remove, best-effort Cloudinary cleanup, audit log).
/// Introduced alongside the plan-quota fix: Short-Stay had no delete path at all before this,
/// so an owner could never free up their plan quota by removing one.
/// </summary>
public sealed record DeleteShortStayListingCommand(
    Guid ListingId,
    Guid RequestingUserId,
    string? IpAddress = null) : IRequest<bool>;

public sealed class DeleteShortStayListingCommandHandler
    : IRequestHandler<DeleteShortStayListingCommand, bool>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IBookingRepository _bookings;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<DeleteShortStayListingCommandHandler> _logger;

    public DeleteShortStayListingCommandHandler(
        IShortStayListingRepository listings,
        IBookingRepository bookings,
        IMediaStorageService storage,
        IUnitOfWork uow,
        IAuditLogService auditLogs,
        ILogger<DeleteShortStayListingCommandHandler> logger)
    {
        _listings = listings;
        _bookings = bookings;
        _storage = storage;
        _uow = uow;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<bool> Handle(DeleteShortStayListingCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.RequestingUserId)
            throw new ForbiddenException("Only the listing owner can delete it.");

        // Owner decision (2026-09-24): a listing with bookings still in progress cannot be deleted. After a
        // delete those bookings could no longer be approved, checked in, completed or even cancelled (their
        // listing is filtered out), so the host has to finish or cancel them first.
        if (await _bookings.HasActiveBookingsForListingAsync(listing.Id, ct))
            throw new ConflictException(
                "This listing has bookings in progress. Complete, reject or cancel them before deleting the listing.");

        var oldValue = JsonSerializer.Serialize(new
        {
            listingId = listing.Id,
            listing.Title,
            listing.OwnerId,
            listing.IsPublished
        });

        // Capture Cloudinary PublicIds before the row disappears behind the soft-delete
        // query filter — same rationale as DeletePropertyCommandHandler's identical step.
        var photoPublicIds = listing.Photos
            .Where(p => !string.IsNullOrWhiteSpace(p.PublicId))
            .Select(p => p.PublicId)
            .ToList();

        listing.MarkAsDeleted(request.RequestingUserId);

        _listings.Remove(listing);
        await _uow.SaveChangesAsync(ct);

        // Best-effort, deliberately outside the transaction above — one Cloudinary failure
        // must not roll back or block cleanup of the remaining photos.
        foreach (var publicId in photoPublicIds)
        {
            try
            {
                await _storage.DeleteImageAsync(publicId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete Cloudinary image {PublicId} after deleting short-stay listing {ListingId}.",
                    publicId,
                    listing.Id);
            }
        }

        await _auditLogs.LogAsync(
            userId: request.RequestingUserId,
            action: AuditActions.DeleteShortStayListing,
            ipAddress: request.IpAddress,
            oldValue: oldValue,
            newValue: JsonSerializer.Serialize(new
            {
                listingId = listing.Id,
                deleted = true,
                deletedAt = listing.DeletedAt,
                deletedByUserId = request.RequestingUserId
            }),
            ct: ct);

        return true;
    }
}
