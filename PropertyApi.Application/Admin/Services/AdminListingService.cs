using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Admin.Services;

/// <summary>
/// Admin-only listing actions — see IAdminListingService's doc comment. Mirrors
/// ConfirmFeaturedListingPaymentCommandHandler/ConfirmListingExtensionPaymentCommandHandler
/// structurally (begin tx, mutate the domain entity, save, commit) but grants for free
/// instead of settling a pre-existing fee, and — the explicit business rule this exists
/// for — never consults plan/quota. No advisory lock: same reasoning as those two handlers,
/// which don't take one either — a single-row domain mutation with no "count across rows"
/// race to close (unlike ListingQuotaLock's count-then-insert window).
/// </summary>
public sealed class AdminListingService : IAdminListingService
{
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<AdminListingService> _logger;

    public AdminListingService(
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        ILogger<AdminListingService> logger)
    {
        _properties = properties;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<PagedResult<AdminPropertySummaryDto>> GetUserPropertiesAsync(
        Guid userId,
        int page,
        int pageSize,
        string? status,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var all = await _properties.GetByOwnerAsync(userId, ct);

        IEnumerable<Property> filtered = all;
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<PropertyStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            filtered = filtered.Where(p => p.Status == parsedStatus);
        }

        var ordered = filtered.OrderByDescending(p => p.CreatedAt).ToList();

        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSummary)
            .ToArray();

        return new PagedResult<AdminPropertySummaryDto>
        {
            Items = items,
            TotalCount = ordered.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AdminOperationResult> FeatureListingAsync(
        Guid propertyId,
        int days,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (!IsValidDuration(days, out var durationError))
        {
            return AdminOperationResult.BadRequest(durationError);
        }

        var property = await _properties.GetByIdAsync(propertyId, ct);
        if (property is null)
        {
            return new AdminOperationResult { Succeeded = false, NotFound = true, Message = "Listing not found." };
        }

        var oldSnapshot = SnapshotListing(property);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Rejects an Expired listing on purpose (property.MarkFeatured's own guard) —
            // the admin must extend it first; this is not bypassed here, matching how
            // ConfirmFeaturedListingPaymentCommandHandler never bypasses it either.
            property.MarkFeatured(TimeSpan.FromDays(days), DateTime.UtcNow);

            _properties.Update(property);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.ListingFeaturedByAdmin,
            performedByUserId, ipAddress, property.OwnerId, propertyId, reason,
            oldSnapshot, SnapshotListing(property), ct);

        _logger.LogInformation(
            "Listing featured by admin. PropertyId={PropertyId}, Days={Days}, FeaturedUntil={FeaturedUntil}, By={By}",
            propertyId, days, property.FeaturedUntil, performedByUserId);

        return AdminOperationResult.Ok($"Listing featured until {property.FeaturedUntil:O}.");
    }

    public async Task<AdminOperationResult> UnfeatureListingAsync(
        Guid propertyId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var property = await _properties.GetByIdAsync(propertyId, ct);
        if (property is null)
        {
            return new AdminOperationResult { Succeeded = false, NotFound = true, Message = "Listing not found." };
        }

        var oldSnapshot = SnapshotListing(property);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            property.ClearFeatured();

            _properties.Update(property);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.ListingUnfeaturedByAdmin,
            performedByUserId, ipAddress, property.OwnerId, propertyId, reason,
            oldSnapshot, SnapshotListing(property), ct);

        _logger.LogInformation(
            "Listing unfeatured by admin. PropertyId={PropertyId}, By={By}",
            propertyId, performedByUserId);

        return AdminOperationResult.Ok("Listing unfeatured.");
    }

    public async Task<AdminOperationResult> ExtendListingAsync(
        Guid propertyId,
        int days,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (!IsValidDuration(days, out var durationError))
        {
            return AdminOperationResult.BadRequest(durationError);
        }

        var property = await _properties.GetByIdAsync(propertyId, ct);
        if (property is null)
        {
            return new AdminOperationResult { Succeeded = false, NotFound = true, Message = "Listing not found." };
        }

        var oldSnapshot = SnapshotListing(property);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Deliberately no plan/quota check — an admin may extend a listing even for a
            // free-plan owner (explicit business rule; ExtendPublication never consulted
            // quota to begin with, quota only ever gates *creating* a new listing).
            property.ExtendPublication(TimeSpan.FromDays(days), DateTime.UtcNow);

            _properties.Update(property);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.ListingExtendedByAdmin,
            performedByUserId, ipAddress, property.OwnerId, propertyId, reason,
            oldSnapshot, SnapshotListing(property), ct);

        _logger.LogInformation(
            "Listing extended by admin. PropertyId={PropertyId}, Days={Days}, NewExpiresAt={ExpiresAt}, By={By}",
            propertyId, days, property.ExpiresAt, performedByUserId);

        return AdminOperationResult.Ok($"Listing extended to {property.ExpiresAt:O}.");
    }

    private static bool IsValidDuration(int days, out string error)
    {
        if (days < ListingLifecyclePolicy.MinAdminGrantDays
            || days > ListingLifecyclePolicy.MaxAdminGrantDays)
        {
            error =
                $"Duration must be between {ListingLifecyclePolicy.MinAdminGrantDays} " +
                $"and {ListingLifecyclePolicy.MaxAdminGrantDays} days.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static AdminPropertySummaryDto ToSummary(Property p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Status = p.Status.ToString(),
        IsPublished = p.IsPublished,
        PublishedAt = p.PublishedAt,
        ExpiresAt = p.ExpiresAt,
        IsFeatured = p.IsFeatured,
        FeaturedUntil = p.FeaturedUntil,
        CreatedAt = p.CreatedAt
    };

    private static object SnapshotListing(Property p) => new
    {
        status = p.Status.ToString(),
        isPublished = p.IsPublished,
        expiresAt = p.ExpiresAt,
        isFeatured = p.IsFeatured,
        featuredUntil = p.FeaturedUntil
    };

    private Task LogAsync(
        string action,
        Guid performedByUserId,
        string? ipAddress,
        Guid targetUserId,
        Guid propertyId,
        string? reason,
        object oldValue,
        object newValue,
        CancellationToken ct)
        => _auditLogs.LogAsync(
            userId: performedByUserId,
            action: action,
            ipAddress: ipAddress,
            oldValue: JsonSerializer.Serialize(new { targetUserId, propertyId, snapshot = oldValue }),
            newValue: JsonSerializer.Serialize(new
            {
                targetUserId,
                propertyId,
                snapshot = newValue,
                reason,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);
}
