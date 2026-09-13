using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Security.Staging;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/operational")]
public sealed class OperationalController : ControllerBase
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public OperationalController(
        AppDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _db = db;
        _configuration = configuration;
        _environment = environment;
    }

    // RELEASE-BLOCKERS-AR.md B-8: this used to answer anyone, anywhere, with the applied
    // migration count/name, PostGIS availability, and Staging's own database/Redis/storage
    // isolation markers — real infrastructure detail with zero authentication. It exists only
    // for Staging release-readiness automation (production-gate.yml's polling loop, the smoke
    // test's technical-health check, both already updated to send the secret header), so it
    // is now gated exactly like StagingTestSupportController's cleanup endpoint: Staging only,
    // plus a constant-time secret compare, 404 rather than 401/403 so the endpoint's
    // existence is not confirmed to an unauthorized caller either.
    [HttpGet("build-info")]
    [AllowAnonymous]
    public async Task<IActionResult> BuildInfo(CancellationToken ct)
    {
        if (!StagingTestSupportAuthorization.IsAuthorized(Request, _configuration, _environment))
            return NotFound();

        var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var postGisVersion = await _db.Database
            .SqlQueryRaw<string>("SELECT PostGIS_Version() AS \"Value\"")
            .SingleAsync(ct);

        return Ok(new
        {
            environment = _environment.EnvironmentName,
            // `Deployment:CommitSha` is a manually-set config value (see .env.example's
            // "REPLACE_WITH_DEPLOYED_GIT_SHA" placeholder) -- nothing updates it per deploy,
            // so on a real Render service it silently goes stale after the first release and
            // never matches the commit actually running. Render injects the real deployed
            // commit automatically as RENDER_GIT_COMMIT for every build (already used the
            // same way in PropertyApiObservabilityExtensions.CreateResource), so prefer that
            // live, always-accurate source first and fall back to the manually configured
            // value only where RENDER_GIT_COMMIT doesn't exist (local/CI/docker-compose runs,
            // which already set Deployment__CommitSha themselves -- see
            // ci/docker-compose.production-gate.yml and performance/docker-compose.performance.yml).
            commitSha = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT")
                ?? _configuration["Deployment:CommitSha"]
                ?? "unknown",
            version = _configuration["Deployment:Version"] ?? "unknown",
            startedAtUtc = StartedAtUtc,
            migration = new
            {
                complete = pending.Length == 0,
                appliedCount = applied.Length,
                latest = applied.LastOrDefault(),
                pendingCount = pending.Length,
                postGisAvailable = !string.IsNullOrWhiteSpace(postGisVersion)
            },
            isolation = new
            {
                environmentId = _configuration["Staging:EnvironmentId"],
                databaseMarker = _configuration["Staging:DatabaseNameMarker"],
                redisMarker = _configuration["Staging:RedisIsolationMarker"],
                storageMarker = _configuration["Staging:StorageIsolationMarker"],
                externalNotificationsDisabled = _configuration.GetValue<bool>(
                    "Staging:ExternalNotificationsDisabled"),
                testSupportEnabled = _environment.IsStaging() &&
                    _configuration.GetValue<bool>("Staging:TestSupport:Enabled"),
                inMemoryMedia = _environment.IsStaging() &&
                    _configuration.GetValue<bool>("Staging:TestSupport:UseInMemoryMedia")
            }
        });
    }

}
