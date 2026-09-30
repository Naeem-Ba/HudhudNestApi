using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.Listings.Commands.DeleteProperty;

public sealed class DeletePropertyCommandHandler
    : IRequestHandler<DeletePropertyCommand, bool>
{
    private readonly IPropertyRepository _repo;
    private readonly IPropertyImageRepository _images;
    private readonly IPropertyOwnershipService _ownership;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLogs;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeletePropertyCommandHandler> _logger;

    public DeletePropertyCommandHandler(
        IPropertyRepository repo,
        IPropertyImageRepository images,
        IPropertyOwnershipService ownership,
        IMediaStorageService storage,
        IUnitOfWork uow,
        IAuditLogService auditLogs,
        IPublisher publisher,
        ILogger<DeletePropertyCommandHandler> logger)
    {
        _repo = repo;
        _images = images;
        _ownership = ownership;
        _storage = storage;
        _uow = uow;
        _auditLogs = auditLogs;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<bool> Handle(
        DeletePropertyCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _ownership.GetOwnedPropertyOrThrowAsync(
            request.PropertyId,
            request.RequestingUserId,
            operation: "delete",
            ct: cancellationToken);

        var oldValue = JsonSerializer.Serialize(new
        {
            propertyId = property.Id,
            property.Title,
            property.OwnerId,
            property.Status,
            property.IsPublished
        });

        // Privacy: capture the Cloudinary PublicIds before the row disappears behind
        // the soft-delete query filter. Nothing else in this codebase ever deletes
        // these remote image binaries once a property is gone (see the identical fix
        // in ListingExpiryHostedService.DeleteAfterGraceAsync for the automated-sweep
        // path) — this was a confirmed data-retention gap (docs/privacy/privacy-gaps.md).
        var propertyWithImages = await _images.GetPropertyWithImagesAsync(
            property.Id,
            cancellationToken);

        var imagePublicIds = propertyWithImages?.Images
            .Where(image => !string.IsNullOrWhiteSpace(image.PublicId))
            .Select(image => image.PublicId!)
            .ToList()
            ?? new List<string>();

        var statusAtDeletion = property.Status;
        property.MarkAsDeleted(request.RequestingUserId);

        _repo.Remove(property);
        await _uow.SaveChangesAsync(cancellationToken);

        // Phase 1 audit F-10: Status never changes here (a live Available listing can be deleted
        // directly), so SocialPublicationLifecyclePolicy's status-transition map can never see
        // this — a dedicated event instead of PropertyStatusChangedEvent (see its own remarks).
        // Fire-and-notify: a broken subscriber must never turn an already-committed delete into a
        // failed request.
        try
        {
            await _publisher.Publish(
                new PropertyDeletedEvent(property.Id, statusAtDeletion, property.DeletedAt ?? DateTime.UtcNow, request.RequestingUserId),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish PropertyDeletedEvent after deleting property {PropertyId}.", property.Id);
        }

        // Best-effort, deliberately outside the transaction above: the property row is
        // already gone from every consumer's point of view (soft-delete query filter),
        // so one Cloudinary failure must not roll that back or block cleanup of the
        // remaining images.
        foreach (var publicId in imagePublicIds)
        {
            try
            {
                await _storage.DeleteImageAsync(publicId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete Cloudinary image {PublicId} after deleting property {PropertyId}.",
                    publicId,
                    property.Id);
            }
        }

        await _auditLogs.LogAsync(
            userId: request.RequestingUserId,
            action: AuditActions.DeleteProperty,
            ipAddress: request.IpAddress,
            oldValue: oldValue,
            newValue: JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                deleted = true,
                deletedAt = property.DeletedAt,
                deletedByUserId = request.RequestingUserId
            }),
            ct: cancellationToken);

        return true;
    }
}
