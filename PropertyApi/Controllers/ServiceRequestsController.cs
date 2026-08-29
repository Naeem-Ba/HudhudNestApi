using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Services.Commands.AcceptServiceRequest;
using PropertyApi.Application.Services.Commands.AddServiceReview;
using PropertyApi.Application.Services.Commands.CancelServiceRequest;
using PropertyApi.Application.Services.Commands.CompleteServiceRequest;
using PropertyApi.Application.Services.Commands.CreateServiceRequest;
using PropertyApi.Application.Services.Commands.RejectServiceRequest;
using PropertyApi.Application.Services.Commands.ScheduleServiceRequest;
using PropertyApi.Application.Services.Commands.StartServiceRequest;
using PropertyApi.Application.Services.Commands.UploadServiceRequestDocument;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Queries.GetMyServiceRequests;
using PropertyApi.Application.Services.Queries.GetProviderServiceRequests;
using PropertyApi.Application.Services.Queries.GetServiceRequestById;
using PropertyApi.Application.Services.Queries.GetServiceRequestDocuments;
using PropertyApi.Application.Services.Queries.GetServiceRequestStatusHistory;
using PropertyApi.Domain.Services.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ServiceRequestsController : ControllerBase
{
    private const long MaxDocumentSize = 5_000_000;

    private readonly ISender _mediator;

    public ServiceRequestsController(ISender mediator) => _mediator = mediator;

    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine([FromQuery] ServiceRequestStatus? status, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyServiceRequestsQuery(GetUserId(), status), ct);
        return Ok(result);
    }

    /// <summary>Provider inbox — ActorUserId is resolved to a ServiceProvider server-side.</summary>
    [HttpGet("provider-inbox")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProviderInbox([FromQuery] ServiceRequestStatus? status, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProviderServiceRequestsQuery(GetUserId(), status), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetServiceRequestByIdQuery(id, GetUserId(), IsAdmin()), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceRequestStatusHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetServiceRequestStatusHistoryQuery(id, GetUserId(), IsAdmin()), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/documents")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceRequestDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocuments(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetServiceRequestDocumentsQuery(id, GetUserId(), IsAdmin()), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(MaxDocumentSize)]
    [EnableRateLimiting("service-request-documents")]
    [ProducesResponseType(typeof(ServiceRequestDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();

        var result = await _mediator.Send(
            new UploadServiceRequestDocumentCommand(
                id, GetUserId(), stream, file.FileName, file.ContentType, file.Length),
            ct);

        return CreatedAtAction(nameof(GetDocuments), new { id }, result);
    }

    [HttpPost]
    [EnableRateLimiting("service-requests")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequestRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateServiceRequestCommand(dto.PropertyId, GetUserId(), dto.ServiceOfferingId, dto.RequesterNote),
            ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}/accept")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(Guid id, [FromBody] AcceptServiceRequestRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AcceptServiceRequestCommand(id, GetUserId(), dto.QuotedPrice, dto.QuotedPriceCurrencyId, dto.ProviderNote),
            ct);

        return Ok(result);
    }

    [HttpPut("{id:guid}/reject")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectServiceRequestRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectServiceRequestCommand(id, GetUserId(), dto.Reason), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/schedule")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Schedule(Guid id, [FromBody] ScheduleServiceRequestRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new ScheduleServiceRequestCommand(id, GetUserId(), dto.ScheduledAt), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/start")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new StartServiceRequestCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/complete")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteServiceRequestRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CompleteServiceRequestCommand(id, GetUserId(), dto.FinalPrice, dto.FinalPriceCurrencyId), ct);

        return Ok(result);
    }

    [HttpPut("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelServiceRequestRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CancelServiceRequestCommand(id, GetUserId(), IsAdmin(), dto.Reason), ct);

        return Ok(result);
    }

    /// <summary>Verified service review — see AddServiceReviewCommandHandler's gating.</summary>
    [HttpPost("{id:guid}/review")]
    [EnableRateLimiting("reviews")]
    [ProducesResponseType(typeof(ServiceReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddReview(Guid id, [FromBody] AddServiceReviewRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AddServiceReviewCommand(id, GetUserId(), dto.Rating, dto.Comment), ct);

        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    private bool IsAdmin() => User.IsInRole(RoleNames.Admin);

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

public sealed record CreateServiceRequestRequest(
    Guid PropertyId, Guid ServiceOfferingId, string? RequesterNote);

public sealed record AcceptServiceRequestRequest(
    decimal? QuotedPrice, int? QuotedPriceCurrencyId, string? ProviderNote);

public sealed record RejectServiceRequestRequest(string Reason);

public sealed record ScheduleServiceRequestRequest(DateTime ScheduledAt);

public sealed record CompleteServiceRequestRequest(decimal? FinalPrice, int? FinalPriceCurrencyId);

public sealed record CancelServiceRequestRequest(string? Reason);

public sealed record AddServiceReviewRequest(int Rating, string? Comment);
