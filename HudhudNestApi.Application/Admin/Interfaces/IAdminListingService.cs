using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Admin.Interfaces;

/// <summary>
/// Admin-only listing actions reachable from a user's "ads" screen — feature/unfeature and
/// extend a specific listing without going through the owner-initiated fee-request +
/// payment-confirmation flow (ConfirmFeaturedListingPaymentCommand /
/// ConfirmListingExtensionPaymentCommand on PropertiesController). Deliberately has no
/// quota/plan check anywhere in this interface's implementation: an admin may extend a
/// listing even for a free-plan owner (explicit business rule) — quota only ever gates
/// *creating* a new listing, never keeping/extending an existing one.
/// </summary>
public interface IAdminListingService
{
    Task<PagedResult<AdminPropertySummaryDto>> GetUserPropertiesAsync(
        Guid userId,
        int page,
        int pageSize,
        string? status,
        CancellationToken ct = default);

    Task<AdminOperationResult> FeatureListingAsync(
        Guid propertyId,
        int days,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> UnfeatureListingAsync(
        Guid propertyId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> ExtendListingAsync(
        Guid propertyId,
        int days,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);
}
