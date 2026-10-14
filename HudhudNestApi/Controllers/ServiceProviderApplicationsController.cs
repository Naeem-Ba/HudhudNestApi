using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Services.ProviderOnboarding;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Controllers;

/// <summary>
/// The applicant's side of joining the marketplace as a service provider. Signed-in only: the
/// application is tied to the caller's account from the start, every lookup is scoped to that
/// account on the server, and a foreign application id answers 404. Admin decisions live in
/// <see cref="AdminServiceProvidersController"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ServiceProviderApplicationsController : ControllerBase
{
    private readonly ISender _mediator;

    public ServiceProviderApplicationsController(ISender mediator) => _mediator = mediator;

    [HttpPost]
    [EnableRateLimiting("provider-applications")]
    [ProducesResponseType(typeof(ServiceProviderApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit([FromBody] SubmitServiceProviderApplicationRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new SubmitServiceProviderApplicationCommand(GetUserId(), dto.ToInput(), dto.TermsAccepted), ct);
        return CreatedAtAction(nameof(GetMine), null, result);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceProviderApplicationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMyServiceProviderApplicationsQuery(GetUserId()), ct));

    [HttpPut("{id:guid}/respond")]
    [EnableRateLimiting("provider-applications")]
    [ProducesResponseType(typeof(ServiceProviderApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondServiceProviderApplicationRequest dto, CancellationToken ct)
        => Ok(await _mediator.Send(
            new RespondToServiceProviderApplicationCommand(GetUserId(), id, dto.ToInput(), dto.Message), ct));

    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(typeof(ServiceProviderApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new WithdrawServiceProviderApplicationCommand(GetUserId(), id), ct));

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

/// <summary>
/// Only the fields an applicant may set. Status, applicant id, reviewer and provider link are
/// never bound from the body (no mass assignment).
/// </summary>
public record ServiceProviderApplicationRequestBase(
    ServiceProviderApplicantType ApplicantType,
    string DisplayName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory>? Categories,
    IReadOnlyList<int>? GovernorateIds,
    string Experience,
    string? CompanyName,
    string? LicenseNumber)
{
    public ServiceProviderApplicationInput ToInput() => new(
        ApplicantType,
        DisplayName ?? string.Empty,
        ContactName ?? string.Empty,
        ContactEmail,
        ContactPhone,
        Categories ?? [],
        GovernorateIds ?? [],
        Experience ?? string.Empty,
        CompanyName,
        LicenseNumber);
}

public sealed record SubmitServiceProviderApplicationRequest(
    ServiceProviderApplicantType ApplicantType,
    string DisplayName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory>? Categories,
    IReadOnlyList<int>? GovernorateIds,
    string Experience,
    string? CompanyName,
    string? LicenseNumber,
    bool TermsAccepted)
    : ServiceProviderApplicationRequestBase(
        ApplicantType, DisplayName, ContactName, ContactEmail, ContactPhone,
        Categories, GovernorateIds, Experience, CompanyName, LicenseNumber);

public sealed record RespondServiceProviderApplicationRequest(
    ServiceProviderApplicantType ApplicantType,
    string DisplayName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<ServiceCategory>? Categories,
    IReadOnlyList<int>? GovernorateIds,
    string Experience,
    string? CompanyName,
    string? LicenseNumber,
    string Message)
    : ServiceProviderApplicationRequestBase(
        ApplicantType, DisplayName, ContactName, ContactEmail, ContactPhone,
        Categories, GovernorateIds, Experience, CompanyName, LicenseNumber);
