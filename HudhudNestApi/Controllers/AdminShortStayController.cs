using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.ShortStay.Admin;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Administration of short-stay listings (QA Phase 9, P9-D11): list them and take one down with a reason or
/// restore it. Admin-only on the class; the acting admin always comes from the token.
/// </summary>
[ApiController]
[Route("api/admin/short-stay")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminShortStayController : ControllerBase
{
    private readonly ISender _mediator;

    public AdminShortStayController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminShortStayListingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? search,
        [FromQuery] bool? takenDown,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new SearchAdminShortStayListingsQuery(
            new AdminShortStayListingFilter(search, takenDown, page, pageSize)), ct));

    [HttpPost("{id:guid}/take-down")]
    [ProducesResponseType(typeof(AdminShortStayListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> TakeDown(Guid id, [FromBody] AdminShortStayReasonRequest body, CancellationToken ct)
        => Ok(await _mediator.Send(new TakeDownShortStayListingCommand(AdminId(), id, body.Reason, ClientIp()), ct));

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(typeof(AdminShortStayListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromBody] AdminShortStayReasonRequest? body, CancellationToken ct)
        => Ok(await _mediator.Send(new RestoreShortStayListingCommand(AdminId(), id, body?.Reason, ClientIp()), ct));

    private Guid AdminId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record AdminShortStayReasonRequest(string? Reason);
