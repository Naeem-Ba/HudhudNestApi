using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Agencies.Commands.AddAgencyMember;
using PropertyApi.Application.Agencies.Commands.CreateAgency;
using PropertyApi.Application.Agencies.Commands.DeactivateAgency;
using PropertyApi.Application.Agencies.Commands.RemoveAgencyMember;
using PropertyApi.Application.Agencies.Commands.UpdateAgency;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Queries.GetAgencyBySlug;
using PropertyApi.Application.Agencies.Queries.GetMyAgency;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// Real-estate offices: registration, membership and the public agency page.
///
/// Authorization note — the AgencyOwner role gates the route, but it is never the whole
/// check. Every handler behind these routes also compares the caller to the target
/// agency's OwnerUserId, because holding the role says a user owns *an* agency, not
/// *this* one.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AgenciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AgenciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── GET /api/agencies/{slug} ─────────────────────────────────
    // Public agency page. Anonymous by design; must be registered in
    // PublicEndpointPolicyTests.ApprovedAnonymousEndpoints or the build fails.
    //
    // RELEASE-BLOCKERS-AR.md B-7: this had no rate limit at all — an anonymous caller could
    // hit it without limit, for either denial-of-service or bulk scraping every agency page.
    [HttpGet("{slug}")]
    [AllowAnonymous]
    [EnableRateLimiting("agencies-public")]
    [ProducesResponseType(typeof(AgencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var agency = await _mediator.Send(new GetAgencyBySlugQuery(slug), ct);
        return Ok(agency);
    }

    // ── GET /api/agencies/me ─────────────────────────────────────
    // The caller's own agency, or 204 when they are independent.
    //
    // 204 rather than 404: "you do not belong to an agency" is a normal state for most
    // accounts, and reporting it as Not Found would have every client treating the default
    // case as an error.
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(AgencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var agency = await _mediator.Send(new GetMyAgencyQuery(userId.Value), ct);

        return agency is null ? NoContent() : Ok(agency);
    }

    // ── POST /api/agencies ───────────────────────────────────────
    // Register an agency. Any authenticated user may do this once; the AgencyOwner role is
    // granted by the handler as a result, so it cannot be required here.
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(AgencyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAgencyRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var agency = await _mediator.Send(
            new CreateAgencyCommand(
                Name: request.Name,
                Slug: request.Slug,
                CountryCode: request.CountryCode,
                Description: request.Description,
                ContactEmail: request.ContactEmail,
                ContactPhone: request.ContactPhone,
                City: request.City,
                LicenseNumber: request.LicenseNumber,
                RequestingUserId: userId.Value,
                IpAddress: GetClientIp()),
            ct);

        return CreatedAtAction(nameof(GetBySlug), new { slug = agency.Slug }, agency);
    }

    // ── PUT /api/agencies/{agencyId} ──────────────────────────────
    // Owner-only profile edit. RELEASE-BLOCKERS-AR.md B-4: Agency.UpdateProfile already
    // existed on the domain entity — this is the missing thin command layer around it.
    [HttpPut("{agencyId:guid}")]
    [Authorize(Roles = RoleNames.AgencyOwner)]
    [ProducesResponseType(typeof(AgencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid agencyId,
        [FromBody] UpdateAgencyRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var agency = await _mediator.Send(
            new UpdateAgencyCommand(
                AgencyId: agencyId,
                Name: request.Name,
                Description: request.Description,
                ContactEmail: request.ContactEmail,
                ContactPhone: request.ContactPhone,
                City: request.City,
                RequestingUserId: userId.Value),
            ct);

        return Ok(agency);
    }

    // ── DELETE /api/agencies/{agencyId} ───────────────────────────
    // "Delete" maps to the agency's existing reversible Deactivate() — see B-4's decision
    // note on DeactivateAgencyCommand. Idempotent: deactivating twice is not an error.
    [HttpDelete("{agencyId:guid}")]
    [Authorize(Roles = RoleNames.AgencyOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid agencyId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await _mediator.Send(
            new DeactivateAgencyCommand(AgencyId: agencyId, RequestingUserId: userId.Value),
            ct);

        return NoContent();
    }

    // ── POST /api/agencies/{agencyId}/members ────────────────────
    [HttpPost("{agencyId:guid}/members")]
    [Authorize(Roles = RoleNames.AgencyOwner)]
    [ProducesResponseType(typeof(AgencyMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMember(
        Guid agencyId,
        [FromBody] AddAgencyMemberRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var member = await _mediator.Send(
            new AddAgencyMemberCommand(
                AgencyId: agencyId,
                MemberUserId: request.UserId,
                RequestingUserId: userId.Value,
                IpAddress: GetClientIp()),
            ct);

        return StatusCode(StatusCodes.Status201Created, member);
    }

    // ── DELETE /api/agencies/{agencyId}/members/{memberUserId} ───
    // Owner removing a member, or a member leaving. [Authorize] rather than a role gate:
    // a member leaving holds AgencyAgent, not AgencyOwner, and the handler distinguishes
    // the two cases.
    [HttpDelete("{agencyId:guid}/members/{memberUserId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(
        Guid agencyId,
        Guid memberUserId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await _mediator.Send(
            new RemoveAgencyMemberCommand(
                AgencyId: agencyId,
                MemberUserId: memberUserId,
                RequestingUserId: userId.Value,
                IpAddress: GetClientIp()),
            ct);

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// Request body for agency registration. Separate from the command so the caller cannot
/// supply RequestingUserId — that comes from the token, never from the body.
/// </summary>
public sealed record CreateAgencyRequest(
    string Name,
    string Slug,
    string CountryCode,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? City,
    string? LicenseNumber);

public sealed record AddAgencyMemberRequest(Guid UserId);

/// <summary>
/// Request body for agency profile updates — the same fields Agency.UpdateProfile accepts.
/// Slug, CountryCode and LicenseNumber are intentionally absent: the slug is immutable once
/// shared and links to it would break, the country was fixed at creation, and the licence
/// number is a legal field that Agency.SetLicenseNumber's own doc comment says must never be
/// swept along by a bulk profile save. There is currently no endpoint to change the licence
/// number after creation at all — that gap is real but out of scope for this change.
/// </summary>
public sealed record UpdateAgencyRequest(
    string Name,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? City);
