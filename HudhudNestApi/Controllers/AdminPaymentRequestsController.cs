using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Listings.Commands.ConfirmFeaturedListingPayment;
using HudhudNestApi.Application.Listings.Commands.ConfirmListingExtensionPayment;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Validation;

namespace HudhudNestApi.Controllers;

/// <summary>
/// The administrator's queue of paid things users asked for — paid plans, featured
/// placement and listing extensions. All are requested by the user and only activated here, after the
/// administrator has confirmed the payment arrived (there is no payment gateway).
///
/// A controller of its own rather than more actions on AdminController: that one is
/// constructed directly by tests with four services, and this queue is a separate concern.
/// </summary>
[ApiController]
[Route("api/admin/payment-requests")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminPaymentRequestsController : ControllerBase
{
    private readonly IAdminPaymentRequestService _requests;
    private readonly IAdminSubscriptionService _subscriptions;
    private readonly ISender _sender;

    public AdminPaymentRequestsController(
        IAdminPaymentRequestService requests,
        IAdminSubscriptionService subscriptions,
        ISender sender)
    {
        _requests = requests;
        _subscriptions = subscriptions;
        _sender = sender;
    }

    // GET /api/admin/payment-requests — pending plan requests and pending featured requests.
    [HttpGet]
    [ProducesResponseType(typeof(PendingPaymentRequestsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPending(CancellationToken ct)
        => Ok(await _requests.GetPendingAsync(ct));

    // POST /api/admin/payment-requests/plans/{requestId}/approve  { durationDays, reason? }
    [HttpPost("plans/{requestId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApprovePlan(
        Guid requestId,
        [FromBody] ApprovePlanRequestBody body,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _subscriptions.ApprovePlanRequestAsync(
            requestId, body.DurationDays, body.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/payment-requests/plans/{requestId}/reject  { reason? }
    [HttpPost("plans/{requestId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectPlan(
        Guid requestId,
        [FromBody] RejectPaymentRequestBody body,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _subscriptions.RejectPlanRequestAsync(
            requestId, body.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/payment-requests/featured/{transactionId}/approve
    // Same effect as POST /api/properties/featured/{id}/confirm — the one place a listing is
    // promoted — exposed here so the whole queue is worked from one place.
    [HttpPost("featured/{transactionId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveFeatured(Guid transactionId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null) return Unauthorized();

        var featuredUntil = await _sender.Send(
            new ConfirmFeaturedListingPaymentCommand(transactionId, actorId.Value), ct);

        return Ok(new { featuredUntil });
    }

    // POST /api/admin/payment-requests/featured/{transactionId}/reject  { reason? }
    [HttpPost("featured/{transactionId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectFeatured(
        Guid transactionId,
        [FromBody] RejectPaymentRequestBody body,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _requests.RejectFeaturedAsync(
            transactionId, body.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    // POST /api/admin/payment-requests/extensions/{transactionId}/approve
    // Same effect as POST /api/properties/extension/{id}/confirm — the one place a listing is
    // extended — exposed here so the whole queue is worked from one place (QA Phase 13, P13-D02).
    [HttpPost("extensions/{transactionId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveExtension(Guid transactionId, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null) return Unauthorized();

        var expiresAt = await _sender.Send(
            new ConfirmListingExtensionPaymentCommand(transactionId, actorId.Value), ct);

        return Ok(new { expiresAt });
    }

    // POST /api/admin/payment-requests/extensions/{transactionId}/reject  { reason? }
    [HttpPost("extensions/{transactionId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectExtension(
        Guid transactionId,
        [FromBody] RejectPaymentRequestBody body,
        CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _requests.RejectExtensionAsync(
            transactionId, body.Reason, actorId.Value, GetClientIp(), ct);

        return ToActionResult(result);
    }

    public sealed record ApprovePlanRequestBody(int DurationDays, [AdminReason] string? Reason = null);

    public sealed record RejectPaymentRequestBody([AdminReason] string? Reason = null);

    private IActionResult ToActionResult(AdminOperationResult result)
    {
        if (result.Succeeded) return Ok(new { message = result.Message });
        if (result.NotFound) return NotFound(new { message = result.Message });
        if (result.Conflict) return Conflict(new { message = result.Message });

        return BadRequest(new { message = result.Message, errors = result.Errors });
    }

    private Guid? GetCurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
