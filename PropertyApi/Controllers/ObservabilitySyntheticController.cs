using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Infrastructure.Persistence;
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
        if (!_environment.IsStaging() ||
            !_configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
        {
            return NotFound();
        }

        var expectedKey = _configuration["Staging:TestSupport:CleanupSecret"];
        var suppliedKey = Request.Headers["X-Observability-Test-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expectedKey) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expectedKey),
                System.Text.Encoding.UTF8.GetBytes(suppliedKey)))
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
}
