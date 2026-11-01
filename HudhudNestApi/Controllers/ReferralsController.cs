using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Referrals;
using HudhudNestApi.Domain.Referrals.Entities;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>Growth engine P5: the caller's own invitation code and the status of the people they invited.</summary>
[ApiController]
[Route("api/referrals")]
[Authorize]
public sealed class ReferralsController : ControllerBase
{
    private readonly ISender _sender;

    public ReferralsController(ISender sender) => _sender = sender;

    /// <summary>Creates the code on first call. Lists statuses and dates only, never who signed up.</summary>
    [HttpGet("me")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(typeof(MyReferralsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var userId = CurrentUserId(User);
        if (userId is null)
            return Unauthorized();
        return Ok(await _sender.Send(new GetMyReferralsQuery(userId.Value), ct));
    }

    internal static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Growth engine P5: admin review of referrals. Rewards are recorded here by hand (DECISIONS.md D-11).</summary>
[ApiController]
[Route("api/admin/referrals")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminReferralsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminReferralsController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(AdminReferralPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ReferralStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _sender.Send(new GetAdminReferralsQuery(status, page, pageSize), ct));

    public sealed record RejectBody(string Reason);

    [HttpPost("{id:guid}/reject")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectBody body, CancellationToken ct)
    {
        var admin = ReferralsController.CurrentUserId(User);
        if (admin is null)
            return Unauthorized();
        return await _sender.Send(new RejectReferralCommand(id, admin.Value, body.Reason), ct) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/rewarded")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkRewarded(Guid id, CancellationToken ct)
    {
        var admin = ReferralsController.CurrentUserId(User);
        if (admin is null)
            return Unauthorized();
        return await _sender.Send(new MarkReferralRewardedCommand(id, admin.Value), ct) ? NoContent() : NotFound();
    }
}
