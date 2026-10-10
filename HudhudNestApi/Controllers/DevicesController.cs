using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Push;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Push-notification device registration. The user id always comes from the authenticated
/// principal, never from the body, so one account can neither register nor remove a device for
/// another.
/// </summary>
[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private const int MaxTokenLength = 512;

    private readonly IDeviceTokenService _devices;
    private readonly ICurrentUserService _currentUser;

    public DevicesController(IDeviceTokenService devices, ICurrentUserService currentUser)
    {
        _devices = devices;
        _currentUser = currentUser;
    }

    /// <summary>Registers (or re-binds to this account) the calling app install for push.</summary>
    [HttpPost("push-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register([FromBody] RegisterPushTokenRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > MaxTokenLength)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid push token.");
        }

        if (!Enum.TryParse<DevicePlatform>(request.Platform, ignoreCase: true, out var platform)
            || !Enum.IsDefined(platform))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Platform must be android, ios or web.");
        }

        await _devices.RegisterAsync(userId, request.Token, platform, ct);
        return NoContent();
    }

    /// <summary>Removes the token on sign-out. Idempotent; only the owner's row can be removed.</summary>
    [HttpPost("push-token/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Revoke([FromBody] RevokePushTokenRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        if (!string.IsNullOrWhiteSpace(request.Token) && request.Token.Length <= MaxTokenLength)
        {
            await _devices.UnregisterAsync(userId, request.Token, ct);
        }

        return NoContent();
    }
}

public sealed record RegisterPushTokenRequest(string Token, string Platform);

public sealed record RevokePushTokenRequest(string Token);
