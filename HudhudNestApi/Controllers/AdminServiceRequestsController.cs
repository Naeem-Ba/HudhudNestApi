using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Services.Admin;
using HudhudNestApi.Application.Services.ProviderOnboarding;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// "إدارة الخدمات": every service request in one place, with statistics per category, offering
/// and provider, the reasons both sides gave, and the few admin actions (set the outcome,
/// hide a review). Admin-only on the class; the acting admin always comes from the token.
/// Cancelling a request reuses PUT /api/ServiceRequests/{id}/cancel, which already lets an admin in.
/// </summary>
[ApiController]
[Route("api/admin/service-requests")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminServiceRequestsController : ControllerBase
{
    private readonly ISender _mediator;

    public AdminServiceRequestsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(ServicePageDto<AdminServiceRequestSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? search,
        [FromQuery] ServiceRequestStatus? status,
        [FromQuery] ServiceCategory? category,
        [FromQuery] Guid? offeringId,
        [FromQuery] Guid? providerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool needsAttention = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new SearchAdminServiceRequestsQuery(new AdminServiceRequestFilter(
            search, status, category, offeringId, providerId, from, to, needsAttention, page, pageSize)), ct));

    [HttpGet("stats")]
    [ProducesResponseType(typeof(ServiceStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _mediator.Send(new GetServiceStatsQuery(from, to), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminServiceRequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAdminServiceRequestDetailQuery(id), ct));

    [HttpPut("{id:guid}/outcome")]
    [ProducesResponseType(typeof(AdminServiceRequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetOutcome(Guid id, [FromBody] AdminSetOutcomeBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new AdminSetServiceRequestOutcomeCommand(
            AdminId(), id, body.Completed, body.Reason, body.ProviderNote, body.AdminNote, ClientIp()), ct));

    [HttpPut("reviews/{reviewId:guid}/visibility")]
    [ProducesResponseType(typeof(AdminServiceReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetReviewVisibility(Guid reviewId, [FromBody] ReviewVisibilityBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new AdminSetServiceReviewVisibilityCommand(
            AdminId(), reviewId, body.Hidden, body.Reason, ClientIp()), ct));

    private Guid AdminId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    public sealed record AdminSetOutcomeBody(
        bool Completed, ServiceOutcomeReason? Reason, string? ProviderNote, string AdminNote);

    public sealed record ReviewVisibilityBody(bool Hidden, string? Reason);
}
