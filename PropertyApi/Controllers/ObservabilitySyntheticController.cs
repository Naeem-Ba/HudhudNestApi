using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Observability;
using StackExchange.Redis;

namespace PropertyApi.Controllers;

[ApiController]
[Route("internal/observability")]
public sealed class ObservabilitySyntheticController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ObservabilitySyntheticController(
        AppDbContext dbContext,
        IConnectionMultiplexer redis,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _redis = redis;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpPost("synthetic")]
    [AllowAnonymous]
    public async Task<IActionResult> Execute(CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            return NotFound();
        }

        await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        await _redis.GetDatabase().PingAsync();

        using var response = await _httpClientFactory
            .CreateClient("ObservabilitySynthetic")
            .GetAsync("", cancellationToken);
        response.EnsureSuccessStatusCode();

        return Ok(new { result = "ok" });
    }

    public sealed record AlertTestStateRequest(bool Firing);

    // Lets scripts/verify-observability.sh drive a real Prometheus alert through a full
    // firing -> resolved lifecycle without ever writing to Prometheus's rule files at runtime:
    // the rule itself (observability/render/prometheus/rules) is permanent and just watches
    // this gauge. Same Staging-only + shared-secret gate as the sibling "synthetic" endpoint.
    [HttpPost("synthetic/alert-test-state")]
    [AllowAnonymous]
    public IActionResult SetAlertTestState([FromBody] AlertTestStateRequest request)
    {
        if (!IsAuthorized())
        {
            return NotFound();
        }

        PropertyApiTelemetry.SetSyntheticAlertTestState(request.Firing);
        return Ok(new { result = "ok", firing = request.Firing });
    }

    private bool IsAuthorized()
    {
        if (!_environment.IsStaging() ||
            !_configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
        {
            return false;
        }

        var expectedKey = _configuration["Staging:TestSupport:CleanupSecret"];
        var suppliedKey = Request.Headers["X-Observability-Test-Key"].ToString();
        return !string.IsNullOrWhiteSpace(expectedKey) &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expectedKey),
                System.Text.Encoding.UTF8.GetBytes(suppliedKey));
    }
}
