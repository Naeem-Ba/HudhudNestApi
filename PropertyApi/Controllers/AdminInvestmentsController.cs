using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Investments.Commands.AddInvestmentDocument;
using PropertyApi.Application.Investments.Commands.AddInvestmentUpdate;
using PropertyApi.Application.Investments.Commands.ApproveInvestmentProject;
using PropertyApi.Application.Investments.Commands.CloseInvestmentProject;
using PropertyApi.Application.Investments.Commands.CreateInvestmentProject;
using PropertyApi.Application.Investments.Commands.PublishInvestmentProject;
using PropertyApi.Application.Investments.Commands.RejectInvestmentProject;
using PropertyApi.Application.Investments.Commands.RemoveInvestmentDocument;
using PropertyApi.Application.Investments.Commands.ScheduleInvestmentProject;
using PropertyApi.Application.Investments.Commands.SetInvestmentDocumentVisibility;
using PropertyApi.Application.Investments.Commands.SetInvestmentProjectFinancials;
using PropertyApi.Application.Investments.Commands.SetInvestmentProjectRiskAssessment;
using PropertyApi.Application.Investments.Commands.SubmitInvestmentProjectForReview;
using PropertyApi.Application.Investments.Commands.SuspendInvestmentProject;
using PropertyApi.Application.Investments.Commands.UpdateInvestmentProject;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Queries.GetAdminInvestmentDocuments;
using PropertyApi.Application.Investments.Queries.GetAdminInvestmentProjects;
using PropertyApi.Application.Investments.Queries.GetInvestmentProjectReview;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// Admin/staff management of Investment Discovery projects — creation, the review/publish
/// workflow, and financials/risk/document/update management. State transitions are explicit
/// Commands, never an arbitrary PUT of Status (Phase 1 spec §5/§14).
/// </summary>
[ApiController]
[Route("api/admin/investments")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminInvestmentsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminInvestmentsController(ISender sender) => _sender = sender;

    [HttpGet("projects")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjects([FromQuery] AdminInvestmentProjectFilterDto filter, CancellationToken ct)
    {
        var result = await _sender.Send(new GetAdminInvestmentProjectsQuery(filter), ct);
        return Ok(result);
    }

    [HttpGet("projects/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectReview(Guid id, CancellationToken ct)
    {
        var review = await _sender.Send(new GetInvestmentProjectReviewQuery(id), ct);
        return review is null ? NotFound() : Ok(review);
    }

    [HttpPost("projects")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateProject([FromBody] CreateInvestmentProjectRequest request, CancellationToken ct)
    {
        var id = await _sender.Send(
            new CreateInvestmentProjectCommand(
                request.PropertyId, GetUserId(), request.Title, request.Description,
                request.ProjectType, request.Currency),
            ct);

        return CreatedAtAction(nameof(GetProjectReview), new { id }, new { id });
    }

    [HttpPut("projects/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] UpdateInvestmentProjectRequest request, CancellationToken ct)
    {
        await _sender.Send(
            new UpdateInvestmentProjectCommand(
                id, request.Title, request.ShortDescription, request.Description, request.ProjectType,
                request.TargetAmount, request.MinimumInvestment, request.MaximumInvestment, request.Currency,
                request.InvestmentTermMonths, request.ExpectedReturnMin, request.ExpectedReturnMax,
                request.StartDate, request.EndDate, request.RaisedAmount),
            ct);
        return NoContent();
    }

    // ── Workflow ──────────────────────────────────────────────────

    [HttpPost("projects/{id:guid}/submit-review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SubmitForReview(Guid id, CancellationToken ct)
    {
        await _sender.Send(new SubmitInvestmentProjectForReviewCommand(id), ct);
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await _sender.Send(new ApproveInvestmentProjectCommand(id), ct);
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectInvestmentProjectRequest request, CancellationToken ct)
    {
        await _sender.Send(new RejectInvestmentProjectCommand(id, request.Reason), ct);
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/schedule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Schedule(Guid id, [FromBody] ScheduleInvestmentProjectRequest request, CancellationToken ct)
    {
        await _sender.Send(new ScheduleInvestmentProjectCommand(id, request.ScheduledPublishAt), ct);
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        await _sender.Send(new PublishInvestmentProjectCommand(id), ct);
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken ct)
    {
        await _sender.Send(new SuspendInvestmentProjectCommand(id), ct);
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        await _sender.Send(new CloseInvestmentProjectCommand(id), ct);
        return NoContent();
    }

    // ── Financials / Risk ─────────────────────────────────────────

    [HttpPut("projects/{id:guid}/financials")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetFinancials(Guid id, [FromBody] SetInvestmentProjectFinancialsRequest request, CancellationToken ct)
    {
        await _sender.Send(
            new SetInvestmentProjectFinancialsCommand(
                id, request.PurchasePrice, request.RenovationCost, request.ConstructionCost, request.Taxes,
                request.NotaryCost, request.BrokerCost, request.FinancingCost, request.OperatingCost,
                request.ContingencyReserve, request.ExpectedRevenue, request.ExpectedProfit),
            ct);
        return NoContent();
    }

    [HttpPut("projects/{id:guid}/risk")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetRisk(Guid id, [FromBody] SetInvestmentProjectRiskRequest request, CancellationToken ct)
    {
        await _sender.Send(
            new SetInvestmentProjectRiskAssessmentCommand(
                id, request.RiskLevel, request.MarketRisk, request.LiquidityRisk, request.ProjectRisk,
                request.FinancingRisk, request.DeveloperRisk, request.RiskScore, request.RiskSummary),
            ct);
        return NoContent();
    }

    // ── Documents ─────────────────────────────────────────────────

    [HttpGet("projects/{id:guid}/documents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllDocuments(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetAdminInvestmentDocumentsQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("projects/{id:guid}/documents")]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddDocument(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] Domain.Investments.Enums.InvestmentDocumentType documentType,
        [FromForm] bool isPublic,
        CancellationToken ct)
    {
        var uploadFile = new InvestmentDocumentUploadFileDto(
            file.OpenReadStream(), file.FileName, file.ContentType, file.Length);

        var documentId = await _sender.Send(
            new AddInvestmentDocumentCommand(id, documentType, uploadFile, isPublic), ct);

        return StatusCode(StatusCodes.Status201Created, new { id = documentId });
    }

    [HttpDelete("projects/{id:guid}/documents/{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        await _sender.Send(new RemoveInvestmentDocumentCommand(id, documentId, GetUserId()), ct);
        return NoContent();
    }

    [HttpPut("projects/{id:guid}/documents/{documentId:guid}/visibility")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDocumentVisibility(
        Guid id, Guid documentId, [FromBody] SetInvestmentDocumentVisibilityRequest request, CancellationToken ct)
    {
        await _sender.Send(new SetInvestmentDocumentVisibilityCommand(id, documentId, request.IsPublic), ct);
        return NoContent();
    }

    // ── Updates ───────────────────────────────────────────────────

    [HttpPost("projects/{id:guid}/updates")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddUpdate(Guid id, [FromBody] AddInvestmentUpdateRequest request, CancellationToken ct)
    {
        var updateId = await _sender.Send(
            new AddInvestmentUpdateCommand(id, request.Title, request.Content, request.UpdateType, GetUserId()), ct);
        return StatusCode(StatusCodes.Status201Created, new { id = updateId });
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

public sealed record CreateInvestmentProjectRequest(
    Guid PropertyId,
    string Title,
    string Description,
    Domain.Investments.Enums.InvestmentProjectType ProjectType,
    string Currency);

public sealed record UpdateInvestmentProjectRequest(
    string Title,
    string? ShortDescription,
    string Description,
    Domain.Investments.Enums.InvestmentProjectType ProjectType,
    decimal TargetAmount,
    decimal MinimumInvestment,
    decimal? MaximumInvestment,
    string Currency,
    int InvestmentTermMonths,
    decimal ExpectedReturnMin,
    decimal ExpectedReturnMax,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal RaisedAmount);

public sealed record RejectInvestmentProjectRequest(string Reason);

public sealed record ScheduleInvestmentProjectRequest(DateTime? ScheduledPublishAt);

public sealed record SetInvestmentProjectFinancialsRequest(
    decimal PurchasePrice,
    decimal RenovationCost,
    decimal ConstructionCost,
    decimal Taxes,
    decimal NotaryCost,
    decimal BrokerCost,
    decimal FinancingCost,
    decimal OperatingCost,
    decimal ContingencyReserve,
    decimal ExpectedRevenue,
    decimal ExpectedProfit);

public sealed record SetInvestmentProjectRiskRequest(
    Domain.Investments.Enums.InvestmentRiskLevel RiskLevel,
    Domain.Investments.Enums.InvestmentRiskLevel MarketRisk,
    Domain.Investments.Enums.InvestmentRiskLevel LiquidityRisk,
    Domain.Investments.Enums.InvestmentRiskLevel ProjectRisk,
    Domain.Investments.Enums.InvestmentRiskLevel FinancingRisk,
    Domain.Investments.Enums.InvestmentRiskLevel DeveloperRisk,
    int RiskScore,
    string RiskSummary);

public sealed record SetInvestmentDocumentVisibilityRequest(bool IsPublic);

public sealed record AddInvestmentUpdateRequest(
    string Title,
    string Content,
    Domain.Investments.Enums.InvestmentUpdateType UpdateType);
