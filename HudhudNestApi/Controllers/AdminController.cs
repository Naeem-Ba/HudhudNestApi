using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IAdminSubscriptionService _subscriptions;
    private readonly IAdminListingService _listings;
    private readonly IAdminValuationInquiryService _valuation;

    public AdminController(
        IAdminService admin,
        IAdminSubscriptionService subscriptions,
        IAdminListingService listings,
        IAdminValuationInquiryService valuation)
    {
        _admin = admin;
        _subscriptions = subscriptions;
        _listings = listings;
        _valuation = valuation;
    }

    // GET /api/admin/users?page=1&pageSize=20&role=User&search=&planTier=&accountStatus=
    [HttpGet("users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? role = null,
        [FromQuery] string? search = null,
        [FromQuery] string? planTier = null,
        [FromQuery] string? accountStatus = null,
        CancellationToken ct = default)
    {
        var result = await _admin.GetUsersWithPaginationAsync(
            page,
            pageSize,
            role,
            search,
            planTier,
            accountStatus,
            ct);

        return Ok(new
        {
            total = result.TotalCount,
            result.Page,
            result.PageSize,
            data = result.Items
        });
    }

    // GET /api/admin/users/{id}
    [HttpGet("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserDetail(Guid id, CancellationToken ct)
    {
        var detail = await _admin.GetUserDetailAsync(id, ct);
        return detail is null
            ? NotFound(new { message = "User not found." })
            : Ok(detail);
    }

    // GET /api/admin/users/{id}/properties?page=1&pageSize=20&status=
    [HttpGet("users/{id:guid}/properties")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserProperties(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await _listings.GetUserPropertiesAsync(id, page, pageSize, status, ct);

        return Ok(new
        {
            total = result.TotalCount,
            result.Page,
            result.PageSize,
            data = result.Items
        });
    }

    // POST /api/admin/users/{id}/subscription/activate  { tier, durationDays, reason? }
    [HttpPost("users/{id:guid}/subscription/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateSubscription(
        Guid id,
        [FromBody] ActivateSubscriptionRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _subscriptions.ActivatePlanAsync(
            id, request.Tier, request.DurationDays, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/users/{id}/subscription/extend  { days, reason? }
    [HttpPost("users/{id:guid}/subscription/extend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExtendSubscription(
        Guid id,
        [FromBody] ExtendSubscriptionRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _subscriptions.ExtendSubscriptionAsync(
            id, request.Days, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/users/{id}/subscription/cancel  { reason? }
    [HttpPost("users/{id:guid}/subscription/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelSubscription(
        Guid id,
        [FromBody] CancelSubscriptionRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _subscriptions.CancelSubscriptionAsync(
            id, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/properties/{propertyId}/feature  { days?, reason? }
    [HttpPost("properties/{propertyId:guid}/feature")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FeatureListing(
        Guid propertyId,
        [FromBody] FeatureListingRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var days = request.Days ?? 30; // ListingLifecyclePolicy.FeaturedPeriod's day count

        var result = await _listings.FeatureListingAsync(
            propertyId, days, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/properties/{propertyId}/unfeature  { reason? }
    [HttpPost("properties/{propertyId:guid}/unfeature")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnfeatureListing(
        Guid propertyId,
        [FromBody] UnfeatureListingRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _listings.UnfeatureListingAsync(
            propertyId, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/properties/{propertyId}/extend  { days, reason? }
    [HttpPost("properties/{propertyId:guid}/extend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExtendListing(
        Guid propertyId,
        [FromBody] ExtendListingRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _listings.ExtendListingAsync(
            propertyId, request.Days, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // GET /api/admin/roles
    [HttpGet("roles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRoles()
        => Ok(_admin.GetRoles());

    // PUT /api/admin/users/{id}/role
    [HttpPut("users/{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _admin.SetUserRoleAsync(
            id,
            request.Role,
            actorId.Value,
            GetClientIp(),
            ct);

        return ToActionResult(result);
    }

    // POST /api/admin/users/{id}/roles/{role}
    [HttpPost("users/{id:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        Guid id,
        string role,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _admin.AssignRoleAsync(
            id,
            role,
            actorId.Value,
            GetClientIp(),
            ct);

        return ToActionResult(result);
    }

    // DELETE /api/admin/users/{id}/roles/{role}
    [HttpDelete("users/{id:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(
        Guid id,
        string role,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _admin.RemoveRoleAsync(
            id,
            role,
            actorId.Value,
            GetClientIp(),
            ct);

        return ToActionResult(result);
    }

    // DELETE /api/admin/users/{id}
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableUser(
        Guid id,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _admin.DisableUserAsync(id, actorId.Value, GetClientIp(), ct);

        if (result.NotFound)
            return NotFound(new { message = result.Message });

        if (!result.Succeeded)
            return BadRequest(new { message = result.Message, errors = result.Errors });

        return NoContent();
    }

    // ── Stage 8 — Valuation module Admin Dashboard ──────────────────────

    // GET /api/admin/valuation-inquiries?page=1&pageSize=20&status=
    [HttpGet("valuation-inquiries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetValuationInquiries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await _valuation.GetInquiriesAsync(page, pageSize, status, ct);

        return Ok(new
        {
            total = result.TotalCount,
            result.Page,
            result.PageSize,
            data = result.Items
        });
    }

    // GET /api/admin/valuation-offices/statistics
    [HttpGet("valuation-offices/statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetValuationOfficeStatistics(CancellationToken ct)
    {
        var stats = await _valuation.GetOfficeStatisticsAsync(ct);
        return Ok(stats);
    }

    // POST /api/admin/valuation-offices/{agencyId}/flag  { reason }
    [HttpPost("valuation-offices/{agencyId:guid}/flag")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FlagValuationOffice(
        Guid agencyId,
        [FromBody] FlagValuationOfficeRequest request,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _valuation.FlagOfficeForReviewAsync(
            agencyId, request.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/valuation-offices/{agencyId}/unflag
    [HttpPost("valuation-offices/{agencyId:guid}/unflag")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnflagValuationOffice(
        Guid agencyId,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        var result = await _valuation.ClearOfficeReviewFlagAsync(
            agencyId, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    private IActionResult ToActionResult(AdminOperationResult result)
    {
        if (result.Succeeded)
            return Ok(new { message = result.Message });

        if (result.NotFound)
            return NotFound(new { message = result.Message });

        if (result.Conflict)
            return Conflict(new { message = result.Message });

        if (result.Message == "The requested role is invalid.")
        {
            return BadRequest(new
            {
                message = result.Message,
                allowedRoles = result.Errors
            });
        }

        return BadRequest(new
        {
            message = result.Message,
            errors = result.Errors
        });
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record UpdateRoleRequest(string Role);

public sealed record ActivateSubscriptionRequest(string Tier, int DurationDays, string? Reason);

public sealed record ExtendSubscriptionRequest(int Days, string? Reason);

public sealed record CancelSubscriptionRequest(string? Reason);

public sealed record FeatureListingRequest(int? Days, string? Reason);

public sealed record UnfeatureListingRequest(string? Reason);

public sealed record ExtendListingRequest(int Days, string? Reason);

public sealed record FlagValuationOfficeRequest(string Reason);
