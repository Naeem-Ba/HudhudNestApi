using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

[ApiController]
[Route("internal/observability")]
public sealed class ObservabilitySyntheticController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ObservabilitySyntheticController(
        AppDbContext dbContext,
        IDistributedCache cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpPost("synthetic")]
    [AllowAnonymous]
    public async Task<IActionResult> Execute(CancellationToken cancellationToken)
    {
        if (!ObservabilityTestAuthorization.IsAuthorized(Request, _configuration, _environment))
        {
            return NotFound();
        }

        await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

        // IDistributedCache, not IConnectionMultiplexer: the latter is only registered when
        // Redis rate limiting is enabled (Program.cs's useRedisRateLimiting, which defaults
        // to Production only), while the distributed cache Redis connection -- the same one
        // DistributedCacheHealthCheck already uses for /health/ready -- is registered
        // unconditionally whenever a Redis connection string is configured, in every
        // environment.
        var key = $"propertyapi:observability-synthetic:{Guid.NewGuid():N}";
        await _cache.SetStringAsync(
            key, "ok", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
            }, cancellationToken);
        await _cache.RemoveAsync(key, cancellationToken);

        using var response = await _httpClientFactory
            .CreateClient("ObservabilitySynthetic")
            .GetAsync("", cancellationToken);
        response.EnsureSuccessStatusCode();

        return Ok(new { result = "ok" });
    }
}
