using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Infrastructure.Persistence;

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

    [HttpGet("build-info")]
    [AllowAnonymous]
    public async Task<IActionResult> BuildInfo(CancellationToken ct)
    {
        var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var postGisVersion = await _db.Database
            .SqlQueryRaw<string>("SELECT PostGIS_Version() AS \"Value\"")
            .SingleAsync(ct);

        return Ok(new
        {
            environment = _environment.EnvironmentName,
            commitSha = _configuration["Deployment:CommitSha"] ?? "unknown",
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
