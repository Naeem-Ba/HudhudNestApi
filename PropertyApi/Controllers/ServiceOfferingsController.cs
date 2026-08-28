using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Common.DTOs;
using PropertyApi.Application.Services.Commands.CreateServiceOffering;
using PropertyApi.Application.Services.Commands.UpdateServiceOffering;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Queries.GetActiveOfferingsByCategory;
using PropertyApi.Application.Services.Queries.GetServiceCategories;
using PropertyApi.Application.Services.Queries.GetServiceOfferingById;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ServiceOfferingsController : ControllerBase
{
    private readonly ISender _mediator;

    public ServiceOfferingsController(ISender mediator) => _mediator = mediator;

    /// <summary>Static enum-backed catalog — see GetServiceCategoriesQuery's remarks.</summary>
    [HttpGet("categories")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(typeof(IReadOnlyList<LookupItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetServiceCategoriesQuery(), ct);
        return Ok(result);
    }

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceOfferingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory([FromQuery] ServiceCategory category, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetActiveOfferingsByCategoryQuery(category), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(typeof(ServiceOfferingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetServiceOfferingByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Provider self-service — ActorUserId is resolved to a ServiceProvider server-side.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceOfferingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateServiceOfferingRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateServiceOfferingCommand(
                GetUserId(), dto.Category, dto.Title, dto.Description,
                dto.BasePrice, dto.CurrencyId, dto.EstimatedDurationDays),
            ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ServiceOfferingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceOfferingRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateServiceOfferingCommand(
                GetUserId(), id, dto.Title, dto.Description,
                dto.BasePrice, dto.CurrencyId, dto.EstimatedDurationDays, dto.IsActive),
            ct);

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

public sealed record CreateServiceOfferingRequest(
    ServiceCategory Category,
    string Title,
    string? Description,
    decimal? BasePrice,
    int? CurrencyId,
    int? EstimatedDurationDays);

public sealed record UpdateServiceOfferingRequest(
    string Title,
    string? Description,
    decimal? BasePrice,
    int? CurrencyId,
    int? EstimatedDurationDays,
    bool IsActive);
