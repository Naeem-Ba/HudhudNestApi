using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Infrastructure.Persistence;
using StackExchange.Redis;

namespace HudhudNestApi.Controllers;

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
        if (!ObservabilityTestAuthorization.IsAuthorized(Request, _configuration, _environment))
        {
            return NotFound();
        }

        await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

        // IConnectionMultiplexer is now registered unconditionally whenever a Redis
        // connection string exists (Program.cs), independent of Redis rate limiting -- so
        // this works, and is traced by AddRedisInstrumentation(), in every environment.
        await _redis.GetDatabase().PingAsync();

        using var response = await _httpClientFactory
            .CreateClient("ObservabilitySynthetic")
            .GetAsync("", cancellationToken);
        response.EnsureSuccessStatusCode();

        return Ok(new { result = "ok" });
    }
}
