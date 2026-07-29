using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Media;
using PropertyApi.Infrastructure.Persistence;

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
        await _db.SaleDetails.Where(x => propertyIds.Contains(x.PropertyId))
            .ExecuteDeleteAsync(ct);
        await _db.RentalDetails.Where(x => propertyIds.Contains(x.PropertyId))
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
        await _db.OtpCodes.Where(x => phones.Contains(x.PhoneNumber))
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

    private bool IsAuthorizedTestSupportRequest()
    {
        if (!_environment.IsStaging() ||
            !_configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
            return false;

        var configured = _configuration["Staging:TestSupport:CleanupSecret"];
        var supplied = Request.Headers["X-Staging-Smoke-Secret"].ToString();
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(supplied))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }

    private static bool IsValidRunId(string? runId) =>
        !string.IsNullOrWhiteSpace(runId) &&
        runId.Length is >= 8 and <= 80 &&
        runId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

public sealed record StagingSmokeCleanupRequest(
    string RunId,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> PropertyIds);
