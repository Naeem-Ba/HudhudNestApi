using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.SocialDistribution.Commands.ActivateDistributionRule;
using PropertyApi.Application.SocialDistribution.Commands.ActivateSocialChannel;
using PropertyApi.Application.SocialDistribution.Commands.ArchiveDistributionRule;
using PropertyApi.Application.SocialDistribution.Commands.CancelSocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.ConnectSocialAccount;
using PropertyApi.Application.SocialDistribution.Commands.CreateDistributionRule;
using PropertyApi.Application.SocialDistribution.Commands.CreateSocialAccount;
using PropertyApi.Application.SocialDistribution.Commands.CreateSocialChannel;
using PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.DeactivateDistributionRule;
using PropertyApi.Application.SocialDistribution.Commands.DeactivateSocialChannel;
using PropertyApi.Application.SocialDistribution.Commands.DisconnectSocialAccount;
using PropertyApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;
using PropertyApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.QueueSocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.RetrySocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.UpdateDistributionRule;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Queries.GetDistributionRuleById;
using PropertyApi.Application.SocialDistribution.Queries.GetSocialAccountById;
using PropertyApi.Application.SocialDistribution.Queries.GetSocialPublicationById;
using PropertyApi.Application.SocialDistribution.Queries.GetSocialPublicationStatusHistory;
using PropertyApi.Application.SocialDistribution.Queries.ListDistributionRules;
using PropertyApi.Application.SocialDistribution.Queries.ListSocialAccounts;
using PropertyApi.Application.SocialDistribution.Queries.ListSocialChannels;
using PropertyApi.Application.SocialDistribution.Queries.ListSocialPublications;
using PropertyApi.Application.SocialDistribution.Queries.PreviewPropertyDistribution;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// SocialDistribution bounded context API (Phase 3 of Social Sharing &amp; Distribution). Entirely
/// operator-facing — unlike Phase 1/2's anonymous, visitor-facing endpoints, every action here
/// requires the Admin role: this engine posts to AqarTech's OWN social accounts, not something
/// an ordinary user ever triggers. See docs for the RBAC-expansion note (SocialDistributionAdmin/
/// ContentManager/Publisher/Viewer roles the spec sketches are a documented future step — this
/// v1 reuses the existing single Admin role rather than fragmenting RBAC no other part of this
/// codebase uses yet).
/// </summary>
[ApiController]
[Route("api/social-distribution")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class SocialDistributionController : ControllerBase
{
    private readonly ISender _mediator;

    public SocialDistributionController(ISender mediator) => _mediator = mediator;

    // ── Channels ─────────────────────────────────────────────────────────

    [HttpGet("channels")]
    [ProducesResponseType(typeof(IReadOnlyList<SocialChannelDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSocialChannelsQuery(), ct);
        return Ok(result);
    }

    [HttpPost("channels")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialChannelDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateChannel([FromBody] CreateSocialChannelRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSocialChannelCommand(dto.Platform, dto.Name, dto.ConfigurationVersion), ct);
        return CreatedAtAction(nameof(GetChannels), result);
    }

    [HttpPost("channels/{id:guid}/activate")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateChannel(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ActivateSocialChannelCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("channels/{id:guid}/deactivate")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeactivateChannel(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeactivateSocialChannelCommand(id), ct);
        return Ok(result);
    }

    // ── Accounts ─────────────────────────────────────────────────────────

    [HttpGet("accounts")]
    [ProducesResponseType(typeof(PropertyApi.Application.Properties.DTOs.PagedResult<SocialAccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccounts(
        [FromQuery] SocialPlatform? platform,
        [FromQuery] int? governorateId,
        [FromQuery] SocialAccountStatus? status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct)
    {
        var filter = new SocialAccountFilterDto(platform, governorateId, status, page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);
        var result = await _mediator.Send(new ListSocialAccountsQuery(filter), ct);
        return Ok(result);
    }

    [HttpGet("accounts/{id:guid}")]
    [ProducesResponseType(typeof(SocialAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSocialAccountByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("accounts")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialAccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAccount([FromBody] CreateSocialAccountRequest dto, CancellationToken ct)
    {
        var command = new CreateSocialAccountCommand(dto.SocialChannelId, dto.DisplayName, dto.ExternalAccountId, dto.AccountType, dto.GovernorateId);
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetAccountById), new { id = result.Id }, result);
    }

    [HttpPost("accounts/{id:guid}/connect")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConnectAccount(Guid id, [FromBody] ConnectSocialAccountRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConnectSocialAccountCommand(id, dto.CredentialReference), ct);
        return Ok(result);
    }

    [HttpPost("accounts/{id:guid}/disconnect")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisconnectAccount(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DisconnectSocialAccountCommand(id), ct);
        return Ok(result);
    }

    // ── Distribution Rules (Phase 4) ────────────────────────────────────────

    [HttpGet("rules")]
    [ProducesResponseType(typeof(PropertyApi.Application.Properties.DTOs.PagedResult<DistributionRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRules(
        [FromQuery] int? provinceId,
        [FromQuery] int? propertyTypeId,
        [FromQuery] ListingType? transactionType,
        [FromQuery] Guid? socialAccountId,
        [FromQuery] bool? isActive,
        [FromQuery] bool includeArchived,
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct)
    {
        var filter = new DistributionRuleFilterDto(
            provinceId, propertyTypeId, transactionType, socialAccountId, isActive, includeArchived, search,
            page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);

        var result = await _mediator.Send(new ListDistributionRulesQuery(filter), ct);
        return Ok(result);
    }

    [HttpGet("rules/{id:guid}")]
    [ProducesResponseType(typeof(DistributionRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRuleById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDistributionRuleByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("rules")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(DistributionRuleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRule([FromBody] CreateDistributionRuleRequest dto, CancellationToken ct)
    {
        var command = new CreateDistributionRuleCommand(
            dto.Name, dto.Description, dto.ProvinceId, dto.PropertyTypeId, dto.TransactionType,
            dto.SocialAccountId, dto.Priority, dto.StartAt, dto.EndAt, GetUserId());

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetRuleById), new { id = result.Id }, result);
    }

    [HttpPatch("rules/{id:guid}")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(DistributionRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateRule(Guid id, [FromBody] UpdateDistributionRuleRequest dto, CancellationToken ct)
    {
        var command = new UpdateDistributionRuleCommand(
            id, dto.Name, dto.Description, dto.ProvinceId, dto.PropertyTypeId, dto.TransactionType,
            dto.Priority, dto.StartAt, dto.EndAt, GetUserId());

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Logical delete — archives the rule (spec §18: "حذف منطقي أو أرشفة قاعدة"). No row is ever physically removed.</summary>
    [HttpDelete("rules/{id:guid}")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(DistributionRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveRule(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveDistributionRuleCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost("rules/{id:guid}/activate")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(DistributionRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateRule(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ActivateDistributionRuleCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("rules/{id:guid}/deactivate")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(DistributionRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeactivateRule(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeactivateDistributionRuleCommand(id), ct);
        return Ok(result);
    }

    /// <summary>Validates a rule payload's own field-level rules WITHOUT persisting anything (spec §18: "POST /rules/validate") — e.g. for the admin form's live validation before submit.</summary>
    [HttpPost("rules/validate")]
    [ProducesResponseType(typeof(DistributionRuleValidationResult), StatusCodes.Status200OK)]
    public IActionResult ValidateRule([FromBody] CreateDistributionRuleRequest dto)
    {
        var command = new CreateDistributionRuleCommand(
            dto.Name, dto.Description, dto.ProvinceId, dto.PropertyTypeId, dto.TransactionType,
            dto.SocialAccountId, dto.Priority, dto.StartAt, dto.EndAt, GetUserId());

        var validation = new CreateDistributionRuleCommandValidator().Validate(command);

        return Ok(new DistributionRuleValidationResult(
            validation.IsValid,
            validation.Errors.Select(e => e.ErrorMessage).ToList()));
    }

    /// <summary>Read-only preview — which accounts this property would be distributed to right now, and why any matched account would be skipped (spec §18: "معاينة القواعد المطابقة لعقار معين").</summary>
    [HttpPost("evaluate/{propertyId:guid}")]
    [ProducesResponseType(typeof(DistributionPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EvaluateDistribution(Guid propertyId, CancellationToken ct)
    {
        var result = await _mediator.Send(new PreviewPropertyDistributionQuery(propertyId), ct);
        return Ok(result);
    }

    /// <summary>Manually (re)runs distribution for a property right now (spec §18: "POST /dispatch/{propertyId}") — same engine the automatic PropertyPublished trigger uses.</summary>
    [HttpPost("dispatch/{propertyId:guid}")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(DistributionRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DispatchDistribution(Guid propertyId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DispatchPropertyDistributionCommand(propertyId, GetUserId()), ct);
        return Ok(result);
    }

    // ── Publications ─────────────────────────────────────────────────────

    [HttpGet("publications")]
    [ProducesResponseType(typeof(PropertyApi.Application.Properties.DTOs.PagedResult<SocialPublicationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublications(
        [FromQuery] Guid? propertyId,
        [FromQuery] Guid? socialAccountId,
        [FromQuery] SocialPlatform? platform,
        [FromQuery] SocialPublicationStatus? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct)
    {
        var filter = new SocialPublicationFilterDto(
            propertyId, socialAccountId, platform, status, fromDate, toDate,
            page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize);

        var result = await _mediator.Send(new ListSocialPublicationsQuery(filter), ct);
        return Ok(result);
    }

    [HttpGet("publications/{id:guid}")]
    [ProducesResponseType(typeof(SocialPublicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicationById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSocialPublicationByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("publications/{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<SocialPublicationStatusHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicationHistory(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSocialPublicationStatusHistoryQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("publications")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialPublicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePublication([FromBody] CreateSocialPublicationRequest dto, CancellationToken ct)
    {
        var command = new CreateSocialPublicationCommand(
            dto.PropertyId, dto.SocialAccountId, GetUserId(),
            dto.Title, dto.Body, dto.ImageUrl, dto.Hashtags, dto.Language ?? "ar");

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetPublicationById), new { id = result.Id }, result);
    }

    [HttpPost("publications/{id:guid}/queue")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialPublicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> QueuePublication(Guid id, [FromBody] QueueSocialPublicationRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new QueueSocialPublicationCommand(id, GetUserId(), dto.ScheduledAt), ct);
        return Ok(result);
    }

    /// <summary>Publish now — the exact same execution path the dispatch worker uses for a due Queued/Retrying publication.</summary>
    [HttpPost("publications/{id:guid}/publish")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialPublicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishPublication(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new PublishSocialPublicationCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("publications/{id:guid}/retry")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialPublicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryPublication(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RetrySocialPublicationCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost("publications/{id:guid}/cancel")]
    [EnableRateLimiting("social-distribution-write")]
    [ProducesResponseType(typeof(SocialPublicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelPublication(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelSocialPublicationCommand(id, GetUserId()), ct);
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

public sealed record CreateSocialChannelRequest(SocialPlatform Platform, string Name, string? ConfigurationVersion);

public sealed record CreateSocialAccountRequest(
    Guid SocialChannelId, string DisplayName, string ExternalAccountId, SocialAccountType AccountType, int? GovernorateId);

public sealed record ConnectSocialAccountRequest(string? CredentialReference);

public sealed record CreateSocialPublicationRequest(
    Guid PropertyId,
    Guid SocialAccountId,
    string? Title,
    string? Body,
    string? ImageUrl,
    IReadOnlyList<string>? Hashtags,
    string? Language);

public sealed record QueueSocialPublicationRequest(DateTime? ScheduledAt);

public sealed record CreateDistributionRuleRequest(
    string Name,
    string? Description,
    int? ProvinceId,
    int? PropertyTypeId,
    ListingType? TransactionType,
    Guid SocialAccountId,
    int Priority,
    DateTime? StartAt,
    DateTime? EndAt);

public sealed record UpdateDistributionRuleRequest(
    string Name,
    string? Description,
    int? ProvinceId,
    int? PropertyTypeId,
    ListingType? TransactionType,
    int Priority,
    DateTime? StartAt,
    DateTime? EndAt);

public sealed record DistributionRuleValidationResult(bool IsValid, IReadOnlyList<string> Errors);
