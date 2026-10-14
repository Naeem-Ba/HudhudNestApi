using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.ProviderOnboarding;
using HudhudNestApi.Application.Services.Commands.SetServiceProviderVerified;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// "إدارة مزوّدي الخدمات": the admin queue of provider applications and the directory of
/// approved providers. Admin-only on the class, so every action is authorized on the server;
/// the acting admin always comes from the token, never from the body.
/// </summary>
[ApiController]
[Route("api/admin/service-providers")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminServiceProvidersController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly IServiceProviderApplicationRepository _applications;

    public AdminServiceProvidersController(ISender mediator, IServiceProviderApplicationRepository applications)
    {
        _mediator = mediator;
        _applications = applications;
    }

    // ── Applications ────────────────────────────────────────────────

    [HttpGet("applications")]
    [ProducesResponseType(typeof(ServicePageDto<AdminServiceProviderApplicationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchApplications(
        [FromQuery] string? search,
        [FromQuery] ServiceProviderApplicationStatus? status,
        [FromQuery] ServiceCategory? category,
        [FromQuery] int? governorateId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new SearchServiceProviderApplicationsQuery(
            new ServiceProviderApplicationFilter(search, status, category, governorateId, page, pageSize)), ct));

    /// <summary>Badge count for the admin menu: applications waiting on an admin.</summary>
    [HttpGet("applications/pending-count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PendingCount(CancellationToken ct)
        => Ok(new { count = await _applications.CountPendingAsync(ct) });

    [HttpGet("applications/{id:guid}")]
    [ProducesResponseType(typeof(AdminServiceProviderApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplication(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetServiceProviderApplicationForAdminQuery(id), ct));

    [HttpPost("applications/{id:guid}/notes")]
    [ProducesResponseType(typeof(AdminServiceProviderApplicationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddApplicationNoteBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new AddServiceProviderApplicationNoteCommand(
            AdminId(), id, body.Note, body.VisibleToApplicant), ct));

    [HttpPost("applications/{id:guid}/request-info")]
    [ProducesResponseType(typeof(AdminServiceProviderApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestInfo(Guid id, [FromBody] RequestInfoBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new RequestServiceProviderApplicationInfoCommand(
            AdminId(), id, body.Message, ClientIp()), ct));

    [HttpPost("applications/{id:guid}/approve")]
    [ProducesResponseType(typeof(AdminServiceProviderApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveApplicationBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new ApproveServiceProviderApplicationCommand(
            AdminId(), id, body.ApprovedCategories ?? [], body.ApprovedGovernorateIds ?? [],
            body.DisplayName, body.VerificationLevel, body.Note, ClientIp()), ct));

    [HttpPost("applications/{id:guid}/reject")]
    [ProducesResponseType(typeof(AdminServiceProviderApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectApplicationBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new RejectServiceProviderApplicationCommand(
            AdminId(), id, body.Reason, ClientIp()), ct));

    // ── Providers ───────────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(ServicePageDto<AdminServiceProviderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchProviders(
        [FromQuery] string? search,
        [FromQuery] ServiceProviderStatus? status,
        [FromQuery] ServiceCategory? category,
        [FromQuery] int? governorateId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new SearchServiceProvidersQuery(
            new ServiceProviderFilter(search, status, category, governorateId, page, pageSize)), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminServiceProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProvider(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetServiceProviderForAdminQuery(id), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminServiceProviderDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProvider(Guid id, [FromBody] UpdateProviderBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateServiceProviderByAdminCommand(
            AdminId(), id, body.DisplayName, body.Bio, body.ContactEmail, body.ContactPhone,
            body.ApprovedCategories ?? [], body.CoverageGovernorateIds ?? [], ClientIp()), ct));

    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(typeof(AdminServiceProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Suspend(Guid id, [FromBody] StatusReasonBody body, CancellationToken ct)
        => ChangeStatus(id, ServiceProviderStatusAction.Suspend, body.Reason, ct);

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(AdminServiceProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
        => ChangeStatus(id, ServiceProviderStatusAction.Reactivate, null, ct);

    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(typeof(AdminServiceProviderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Revoke(Guid id, [FromBody] StatusReasonBody body, CancellationToken ct)
        => ChangeStatus(id, ServiceProviderStatusAction.Revoke, body.Reason, ct);

    [HttpPut("{id:guid}/verification")]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetVerification(Guid id, [FromBody] SetServiceProviderVerifiedRequest body, CancellationToken ct)
        => Ok(await _mediator.Send(new SetServiceProviderVerifiedCommand(id, body.Level, AdminId(), ClientIp()), ct));

    private async Task<IActionResult> ChangeStatus(
        Guid id, ServiceProviderStatusAction action, string? reason, CancellationToken ct)
        => Ok(await _mediator.Send(new ChangeServiceProviderStatusCommand(AdminId(), id, action, reason, ClientIp()), ct));

    private Guid AdminId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    public sealed record AddApplicationNoteBody(string Note, bool VisibleToApplicant = false);
    public sealed record RequestInfoBody(string Message);
    public sealed record RejectApplicationBody(string Reason);
    public sealed record StatusReasonBody(string? Reason);

    public sealed record ApproveApplicationBody(
        IReadOnlyList<ServiceCategory>? ApprovedCategories,
        IReadOnlyList<int>? ApprovedGovernorateIds,
        string? DisplayName = null,
        ServiceProviderVerificationLevel? VerificationLevel = null,
        string? Note = null);

    public sealed record UpdateProviderBody(
        string DisplayName,
        string? Bio,
        string? ContactEmail,
        string? ContactPhone,
        IReadOnlyList<ServiceCategory>? ApprovedCategories,
        IReadOnlyList<int>? CoverageGovernorateIds);
}
