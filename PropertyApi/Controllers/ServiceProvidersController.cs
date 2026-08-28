using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Services.Commands.CreateServiceProvider;
using PropertyApi.Application.Services.Commands.SetServiceProviderVerified;
using PropertyApi.Application.Services.Commands.UpdateServiceProviderProfile;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Queries.GetMyServiceProvider;
using PropertyApi.Application.Services.Queries.GetProviderReviews;
using PropertyApi.Domain.Services.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ServiceProvidersController : ControllerBase
{
    private readonly ISender _mediator;

    public ServiceProvidersController(ISender mediator) => _mediator = mediator;

    /// <summary>Admin-only — onboards a business as a marketplace ServiceProvider.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateServiceProviderRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateServiceProviderCommand(
                dto.UserId, dto.AgencyId, dto.DisplayName, dto.Bio, dto.ContactEmail, dto.ContactPhone),
            ct);

        return CreatedAtAction(nameof(GetMine), result);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyServiceProviderQuery(GetUserId()), ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMine([FromBody] UpdateServiceProviderProfileRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateServiceProviderProfileCommand(
                GetUserId(), dto.DisplayName, dto.Bio, dto.ContactEmail, dto.ContactPhone),
            ct);

        return Ok(result);
    }

    /// <summary>Admin-only — sets the graded verification level. Never a plain "verified" flag.</summary>
    [HttpPut("{id:guid}/verification")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ServiceProviderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetVerification(
        Guid id, [FromBody] SetServiceProviderVerifiedRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetServiceProviderVerifiedCommand(id, dto.Level), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/reviews")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceReviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviews(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProviderReviewsQuery(id, page, pageSize), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (Guid.TryParse(raw, out var userId))
            return userId;

        throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }
}

public sealed record CreateServiceProviderRequest(
    Guid UserId,
    Guid? AgencyId,
    string DisplayName,
    string? Bio,
    string? ContactEmail,
    string? ContactPhone);

public sealed record UpdateServiceProviderProfileRequest(
    string DisplayName,
    string? Bio,
    string? ContactEmail,
    string? ContactPhone);

public sealed record SetServiceProviderVerifiedRequest(ServiceProviderVerificationLevel Level);
