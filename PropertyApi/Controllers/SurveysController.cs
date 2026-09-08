using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Marketing.Commands.SubmitSurvey;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Queries.GetSurveyResponses;
using PropertyApi.Application.Marketing.Queries.GetSurveyStats;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>Short, progressive willingness-to-pay survey shown after the landing-page
/// waitlist form. See SurveyResponse's class doc comment: every answer is optional.</summary>
[ApiController]
[Route("api/surveys")]
public sealed class SurveysController : ControllerBase
{
    private readonly ISender _sender;

    public SurveysController(ISender sender)
        => _sender = sender;

    [HttpPost("landing")]
    [AllowAnonymous] // Public survey; abuse is bounded by "surveys-submit".
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("surveys-submit")]
    public async Task<IActionResult> SubmitLanding([FromBody] SurveySubmitDto dto, CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var id = await _sender.Send(
            new SubmitSurveyCommand(
                dto.LeadId,
                dto.WillingnessToPay,
                dto.PreferredPaymentModel,
                dto.ExpectedMonthlyPriceUsd,
                dto.ExpectedPerListingPriceUsd,
                dto.AcceptableCommissionPercent,
                dto.MostImportantFeature,
                dto.BiggestProblem,
                dto.SubscriptionBlocker,
                dto.WantsTrialBeforePaying,
                dto.TeamSize,
                dto.PropertyCount,
                dto.UsesSimilarToolCurrently,
                dto.SimilarToolName,
                Source: "landing-page"),
            ct);

        return Ok(new { success = true, id });
    }

    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetSurveyResponsesQuery(page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var result = await _sender.Send(new GetSurveyStatsQuery(), ct);
        return Ok(result);
    }
}
