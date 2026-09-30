using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Agencies.Commands.AcceptAgencyInvitation;
using HudhudNestApi.Application.Agencies.Commands.CreateAgency;
using HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitation;
using HudhudNestApi.Application.Agencies.Commands.DeactivateAgency;
using HudhudNestApi.Application.Agencies.Commands.DeclineAgencyInvitation;
using HudhudNestApi.Application.Agencies.Commands.RemoveAgencyMember;
using HudhudNestApi.Application.Agencies.Commands.SetAgencyLogo;
using HudhudNestApi.Application.Agencies.Commands.TransferAgencyOwnership;
using HudhudNestApi.Application.Agencies.Commands.UpdateAgency;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Queries.GetAgencyBySlug;
using HudhudNestApi.Application.Agencies.Queries.GetMyAgency;
using HudhudNestApi.Application.Agencies.Queries.GetMyAgencyInvitations;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

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
                IpAddress: GetClientIp(),
                GovernorateId: request.GovernorateId,
                DistrictId: request.DistrictId,
                NeighborhoodId: request.NeighborhoodId),
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
                RequestingUserId: userId.Value,
                GovernorateId: request.GovernorateId,
                DistrictId: request.DistrictId,
                NeighborhoodId: request.NeighborhoodId),
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

    // ── POST /api/agencies/{agencyId}/transfer-ownership ──────────
    // Owner-only. B-4b: Agency.TransferOwnership already existed on the domain entity —
    // this is the missing thin command layer, same shape as Update above. The new owner
    // must already be a member of this agency (see the command's doc comment) — nothing
    // here can attach a new member on its own.
    [HttpPost("{agencyId:guid}/transfer-ownership")]
    [Authorize(Roles = RoleNames.AgencyOwner)]
    [ProducesResponseType(typeof(AgencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TransferOwnership(
        Guid agencyId,
        [FromBody] TransferAgencyOwnershipRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var agency = await _mediator.Send(
            new TransferAgencyOwnershipCommand(
                AgencyId: agencyId,
                NewOwnerUserId: request.NewOwnerUserId,
                RequestingUserId: userId.Value,
                IpAddress: GetClientIp()),
            ct);

        return Ok(agency);
    }

    // ── POST /api/agencies/{agencyId}/logo ─────────────────────────
    // Owner-only, multipart/form-data. B-4b: Agency.SetLogo already existed on the domain
    // entity — this is the missing thin command layer. Same shape as
    // POST /api/Users/me/avatar (UsersController): IFormFile → DTO that does not know
    // ASP.NET → Command → Handler validates and uploads via IMediaStorageService.
    [HttpPost("{agencyId:guid}/logo")]
    [Authorize(Roles = RoleNames.AgencyOwner)]
    [RequestSizeLimit(2_500_000)]
    [ProducesResponseType(typeof(AgencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetLogo(
        Guid agencyId,
        // No [FromForm] here: Swashbuckle throws "[FromForm] attribute used with IFormFile"
        // and fails the whole /swagger/v1/swagger.json document — see UploadAvatar above.
        IFormFile? file,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "لم يتم رفع أي صورة." });

        var fileDto = new SetAgencyLogoFileDto(
            file.OpenReadStream(),
            file.FileName,
            file.ContentType,
            file.Length);

        var result = await _mediator.Send(
            new SetAgencyLogoCommand(agencyId, userId.Value, fileDto), ct);

        return result.Status switch
        {
            SetAgencyLogoStatus.Success => Ok(result.Agency),
            SetAgencyLogoStatus.NotFound => NotFound(),
            SetAgencyLogoStatus.Forbidden => Forbid(),
            SetAgencyLogoStatus.ValidationFailed => BadRequest(new { message = result.Message }),
            SetAgencyLogoStatus.StorageFailed => BadRequest(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message })
        };
    }

    // ── POST /api/agencies/{agencyId}/invitations ─────────────────
    // Owner invites a user to join. B-2 (RELEASE-BLOCKERS-AR.md): this used to be
    // POST .../members, which attached the user immediately with no consent step. It now
    // only ever creates a Pending AgencyInvitation — see AcceptInvitation for the step
    // that actually creates the membership.
    [HttpPost("{agencyId:guid}/invitations")]
    [Authorize(Roles = RoleNames.AgencyOwner)]
    [ProducesResponseType(typeof(AgencyInvitationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvitation(
        Guid agencyId,
        [FromBody] CreateAgencyInvitationRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var invitation = await _mediator.Send(
            new CreateAgencyInvitationCommand(
                AgencyId: agencyId,
                TargetUserId: request.UserId,
                RequestingUserId: userId.Value,
                IpAddress: GetClientIp()),
            ct);

        return StatusCode(StatusCodes.Status201Created, invitation);
    }

    // ── GET /api/agencies/invitations/mine ────────────────────────
    // The caller's own invitation inbox — never another user's (RequestingUserId always
    // comes from the token, same as GetMine above).
    [HttpGet("invitations/mine")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<AgencyInvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyInvitations(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var invitations = await _mediator.Send(new GetMyAgencyInvitationsQuery(userId.Value), ct);

        return Ok(invitations);
    }

    // ── POST /api/agencies/invitations/{invitationId}/accept ─────
    // Only the invited user may accept their own invitation — the handler checks
    // TargetUserId against the token, not against anything the client supplies.
    [HttpPost("invitations/{invitationId:guid}/accept")]
    [Authorize]
    [ProducesResponseType(typeof(AgencyMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvitation(Guid invitationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var member = await _mediator.Send(
            new AcceptAgencyInvitationCommand(
                InvitationId: invitationId,
                RequestingUserId: userId.Value,
                IpAddress: GetClientIp()),
            ct);

        return Ok(member);
    }

    // ── POST /api/agencies/invitations/{invitationId}/decline ────
    [HttpPost("invitations/{invitationId:guid}/decline")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeclineInvitation(Guid invitationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await _mediator.Send(
            new DeclineAgencyInvitationCommand(
                InvitationId: invitationId,
                RequestingUserId: userId.Value),
            ct);

        return NoContent();
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
    string? LicenseNumber,

    // الموقع الجغرافي المنظَّم — اختياري بالكامل، حفاظًا على مسار الإنشاء القديم أثناء
    // فترة التوافق (انظر CreateAgencyCommand). لا علاقة له بـ City النصي أعلاه، الذي
    // يبقى كما هو.
    int? GovernorateId = null,
    int? DistrictId = null,
    int? NeighborhoodId = null);

/// <summary>Request body for POST .../invitations — who the owner wants to invite.</summary>
public sealed record CreateAgencyInvitationRequest(Guid UserId);

/// <summary>
/// Request body for POST .../transfer-ownership — who the new owner is. Deliberately the
/// only field: OwnerUserId can never be set through UpdateAgencyRequest, and this is the
/// one place it can move at all.
/// </summary>
public sealed record TransferAgencyOwnershipRequest(Guid NewOwnerUserId);

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
    string? City,

    // الموقع الجغرافي المنظَّم. نفس دلالة UpdateAgencyCommand: استبدال كامل، فما يُرسل
    // هنا — بما فيه null — يحل محل القيمة الحالية دومًا. أرسل الموقع الحالي كاملاً حتى
    // عند تعديل حقل آخر فقط، وإلا سيُمسح.
    int? GovernorateId = null,
    int? DistrictId = null,
    int? NeighborhoodId = null);
