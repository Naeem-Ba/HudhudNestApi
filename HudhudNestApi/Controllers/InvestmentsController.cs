using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Investments.Commands.AddInvestmentToWatchlist;
using HudhudNestApi.Application.Investments.Commands.ExpressInvestmentInterest;
using HudhudNestApi.Application.Investments.Commands.RemoveInvestmentFromWatchlist;
using HudhudNestApi.Application.Investments.Commands.WithdrawInvestmentInterest;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Queries.CalculateInvestmentReturn;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentInterestStatus;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectById;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectDocuments;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectFinancials;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectRisk;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectUpdates;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjects;
using HudhudNestApi.Application.Investments.Queries.GetMyInvestmentInterests;
using HudhudNestApi.Application.Investments.Queries.GetMyInvestmentWatchlist;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Investment Discovery Portal — Phase 1. Public browsing/analysis of investment projects plus
/// authenticated watchlist and "Expression of Interest" actions. No endpoint here ever creates
/// a payment, commitment, or real investment — see docs/investment/PHASE-1-DISCOVERY.md.
/// </summary>
[ApiController]
[Route("api/investments")]
public sealed class InvestmentsController : ControllerBase
{
    private readonly ISender _sender;

    public InvestmentsController(ISender sender) => _sender = sender;

    // ── Public ────────────────────────────────────────────────────

    /// <summary>Published projects only, server-side filtered/paged.</summary>
    [HttpGet("projects")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjects([FromQuery] InvestmentProjectFilterDto filter, CancellationToken ct)
    {
        var result = await _sender.Send(new GetInvestmentProjectsQuery(filter), ct);
        return Ok(result);
    }

    [HttpGet("projects/{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectById(Guid id, CancellationToken ct)
    {
        var project = await _sender.Send(new GetInvestmentProjectByIdQuery(id), ct);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpGet("projects/{id:guid}/financials")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectFinancials(Guid id, CancellationToken ct)
    {
        var financials = await _sender.Send(new GetInvestmentProjectFinancialsQuery(id), ct);
        return financials is null ? NotFound() : Ok(financials);
    }

    [HttpGet("projects/{id:guid}/risk")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectRisk(Guid id, CancellationToken ct)
    {
        var risk = await _sender.Send(new GetInvestmentProjectRiskQuery(id), ct);
        return risk is null ? NotFound() : Ok(risk);
    }

    /// <summary>Only IsPublic documents on a Published project — see Phase 1 spec §20.</summary>
    [HttpGet("projects/{id:guid}/documents")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjectDocuments(Guid id, CancellationToken ct)
    {
        var documents = await _sender.Send(new GetInvestmentProjectDocumentsQuery(id), ct);
        return Ok(documents);
    }

    [HttpGet("projects/{id:guid}/updates")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjectUpdates(Guid id, CancellationToken ct)
    {
        var updates = await _sender.Send(new GetInvestmentProjectUpdatesQuery(id), ct);
        return Ok(updates);
    }

    /// <summary>Educational/indicative only — never persisted, never tied to a payment
    /// (Phase 1 spec §21).</summary>
    [HttpPost("projects/{id:guid}/calculator")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate(Guid id, [FromBody] InvestmentCalculatorRequestDto request, CancellationToken ct)
    {
        var result = await _sender.Send(
            new CalculateInvestmentReturnQuery(id, request.Amount, request.TermMonths), ct);
        return Ok(result);
    }

    // ── Authenticated: Watchlist ─────────────────────────────────

    [HttpGet("watchlist")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWatchlist(CancellationToken ct)
    {
        var watchlist = await _sender.Send(new GetMyInvestmentWatchlistQuery(GetUserId()), ct);
        return Ok(watchlist);
    }

    [HttpPost("projects/{id:guid}/watchlist")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddToWatchlist(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new AddInvestmentToWatchlistCommand(GetUserId(), id), ct);
        return result.Status switch
        {
            InvestmentWatchlistMutationStatus.Success => StatusCode(StatusCodes.Status201Created, new { message = result.Message }),
            InvestmentWatchlistMutationStatus.NotFound => NotFound(new { message = result.Message }),
            InvestmentWatchlistMutationStatus.Conflict => Conflict(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message }),
        };
    }

    [HttpDelete("projects/{id:guid}/watchlist")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFromWatchlist(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new RemoveInvestmentFromWatchlistCommand(GetUserId(), id), ct);
        return result.Status switch
        {
            InvestmentWatchlistMutationStatus.Success => NoContent(),
            InvestmentWatchlistMutationStatus.NotFound => NotFound(),
            _ => BadRequest(new { message = result.Message }),
        };
    }

    // ── Authenticated: Expression of Interest ────────────────────

    [HttpGet("interests")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyInterests(CancellationToken ct)
    {
        var interests = await _sender.Send(new GetMyInvestmentInterestsQuery(GetUserId()), ct);
        return Ok(interests);
    }

    [HttpGet("projects/{id:guid}/interest")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInterestStatus(Guid id, CancellationToken ct)
    {
        var status = await _sender.Send(new GetInvestmentInterestStatusQuery(GetUserId(), id), ct);
        return Ok(new { status });
    }

    /// <summary>"أرغب بالاستثمار" — records interest only. See class-level remark: this never
    /// creates a payment, commitment, or investment (Phase 1 spec §30).</summary>
    [HttpPost("projects/{id:guid}/interest")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExpressInterest(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new ExpressInvestmentInterestCommand(GetUserId(), id), ct);
        return result.Status switch
        {
            InvestmentInterestMutationStatus.Success => StatusCode(StatusCodes.Status201Created, new { message = result.Message, interest = result.Interest }),
            InvestmentInterestMutationStatus.NotFound => NotFound(new { message = result.Message }),
            InvestmentInterestMutationStatus.Conflict => Conflict(new { message = result.Message, interest = result.Interest }),
            _ => BadRequest(new { message = result.Message }),
        };
    }

    [HttpDelete("projects/{id:guid}/interest")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WithdrawInterest(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new WithdrawInvestmentInterestCommand(GetUserId(), id), ct);
        return result.Status switch
        {
            InvestmentInterestMutationStatus.Success => NoContent(),
            InvestmentInterestMutationStatus.NotFound => NotFound(),
            _ => BadRequest(new { message = result.Message }),
        };
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
