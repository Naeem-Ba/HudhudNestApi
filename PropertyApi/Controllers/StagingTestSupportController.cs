using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Media;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Security.Staging;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/staging-test-support")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class StagingTestSupportController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly AppDbContext _db;
    private readonly IMediaStorageService _media;

    public StagingTestSupportController(
        IConfiguration configuration,
        IHostEnvironment environment,
        AppDbContext db,
        IMediaStorageService media)
    {
        _configuration = configuration;
        _environment = environment;
        _db = db;
        _media = media;
    }

    [HttpGet("media/{*publicId}")]
    [AllowAnonymous]
    public IActionResult GetMedia(string publicId)
    {
        if (!_environment.IsStaging() ||
            !_configuration.GetValue<bool>("Staging:TestSupport:UseInMemoryMedia"))
            return NotFound();

        var storage = HttpContext.RequestServices
            .GetService<StagingSmokeMediaStorageService>();
        var media = storage?.GetByPublicId(publicId);

        return media is null
            ? NotFound()
            : File(media.Content, media.ContentType, media.FileName);
    }

    [HttpPost("cleanup")]
    [AllowAnonymous] // Not a JWT endpoint: IsAuthorizedTestSupportRequest gates it on Staging + a constant-time secret compare.
    public async Task<IActionResult> Cleanup(
        [FromBody] StagingSmokeCleanupRequest request,
        CancellationToken ct)
    {
        if (!IsAuthorizedTestSupportRequest())
            return NotFound();

        if (!IsValidRunId(request.RunId) ||
            request.UserIds is not { Count: > 0 and <= 4 } ||
            request.PropertyIds is not { Count: <= 10 })
            return BadRequest(new { message = "Invalid cleanup scope." });

        var userIds = request.UserIds.Distinct().ToArray();
        var propertyIds = request.PropertyIds.Distinct().ToArray();
        var expectedTitle = $"E2E-SMOKE-{request.RunId}";

        var accounts = await _db.UserAccounts
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LastName })
            .ToListAsync(ct);
        var properties = await _db.Properties
            .IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.Id))
            .Select(x => new { x.Id, x.OwnerId, x.Title })
            .ToListAsync(ct);

        if (accounts.Count != userIds.Length ||
            accounts.Any(x => !x.LastName.Contains(request.RunId, StringComparison.Ordinal)) ||
            properties.Count != propertyIds.Length ||
            properties.Any(x => x.Title != expectedTitle || !userIds.Contains(x.OwnerId)))
            return BadRequest(new { message = "Cleanup scope ownership validation failed." });

        var publicIds = await _db.PropertyImages
            .IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.PropertyId))
            .Select(x => x.PublicId)
            .ToListAsync(ct);

        foreach (var publicId in publicIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            await _media.DeleteImageAsync(publicId, ct);

        var phones = await _db.Users
            .IgnoreQueryFilters()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => x.NormalizedPhoneNumber)
            .Where(x => x != null)
            .Cast<string>()
            .ToListAsync(ct);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        await _db.Transactions.IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.PropertyId) ||
                userIds.Contains(x.PayerId) || userIds.Contains(x.ReceiverId))
            .ExecuteDeleteAsync(ct);
        await _db.PropertyReviews.IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.PropertyId) || userIds.Contains(x.ReviewerId))
            .ExecuteDeleteAsync(ct);
        await _db.VisitRequests.IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.PropertyId) || userIds.Contains(x.RequesterId))
            .ExecuteDeleteAsync(ct);
        await _db.Notifications.IgnoreQueryFilters()
            .Where(x => userIds.Contains(x.RecipientId) ||
                (x.PropertyId.HasValue && propertyIds.Contains(x.PropertyId.Value)))
            .ExecuteDeleteAsync(ct);
        await _db.Messages.IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.PropertyId) ||
                userIds.Contains(x.SenderId) || userIds.Contains(x.ReceiverId))
            .ExecuteDeleteAsync(ct);
        await _db.Favorites
            .Where(x => propertyIds.Contains(x.PropertyId) || userIds.Contains(x.UserId))
            .ExecuteDeleteAsync(ct);
        await _db.PropertyAmenities
            .Where(x => propertyIds.Contains(x.PropertyId))
            .ExecuteDeleteAsync(ct);
        await _db.PropertyImages.IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.PropertyId))
            .ExecuteDeleteAsync(ct);

        // SocialDistribution rows are Restrict-FK'd to Properties (historical-record-must-
        // survive-soft-delete rationale — see DistributionRunConfiguration/
        // SocialPublicationConfiguration), but this endpoint hard-deletes the property, and
        // PropertyPublishedDistributionHandler creates a DistributionRun for every publish
        // unconditionally (even with zero matching rules), so every smoke run leaves one behind.
        // Deepest dependents first so none of the Restrict FKs below block the next delete.
        var socialPublicationIds = await _db.SocialPublications
            .Where(x => propertyIds.Contains(x.PropertyId))
            .Select(x => x.Id)
            .ToListAsync(ct);
        await _db.SocialPublicationDeadLetters
            .Where(x => socialPublicationIds.Contains(x.PublicationId))
            .ExecuteDeleteAsync(ct);
        await _db.SocialPublicationStatusHistories
            .Where(x => socialPublicationIds.Contains(x.SocialPublicationId))
            .ExecuteDeleteAsync(ct);
        await _db.SocialMediaAssets
            .Where(x => propertyIds.Contains(x.PropertyId))
            .ExecuteDeleteAsync(ct);
        await _db.SocialPublications
            .Where(x => propertyIds.Contains(x.PropertyId))
            .ExecuteDeleteAsync(ct);
        await _db.DistributionRuns
            .Where(x => propertyIds.Contains(x.PropertyId))
            .ExecuteDeleteAsync(ct);

        await _db.Properties.IgnoreQueryFilters()
            .Where(x => propertyIds.Contains(x.Id))
            .ExecuteDeleteAsync(ct);

        await _db.RefreshTokens.IgnoreQueryFilters()
            .Where(x => userIds.Contains(x.UserId))
            .ExecuteDeleteAsync(ct);
        await _db.PhoneOtpChallenges
            .Where(x => (x.UserId.HasValue && userIds.Contains(x.UserId.Value)) ||
                phones.Contains(x.NormalizedPhoneNumber))
            .ExecuteDeleteAsync(ct);
        await _db.AuditLogs.Where(x => x.UserId.HasValue && userIds.Contains(x.UserId.Value))
            .ExecuteDeleteAsync(ct);

        await _db.UserRoles.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
        await _db.UserClaims.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
        await _db.UserLogins.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
        await _db.UserTokens.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
        await _db.UserAccounts.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await _db.Users.IgnoreQueryFilters().Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        return Ok(new
        {
            runId = request.RunId,
            usersRemoved = userIds.Length,
            propertiesRemoved = propertyIds.Length,
            imagesRemoved = publicIds.Count
        });
    }

    private bool IsAuthorizedTestSupportRequest() =>
        StagingTestSupportAuthorization.IsAuthorized(Request, _configuration, _environment);

    private static bool IsValidRunId(string? runId) =>
        !string.IsNullOrWhiteSpace(runId) &&
        runId.Length is >= 8 and <= 80 &&
        runId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

public sealed record StagingSmokeCleanupRequest(
    string RunId,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> PropertyIds);
