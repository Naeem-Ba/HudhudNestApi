using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Security.Staging;
using StackExchange.Redis;

namespace HudhudNestApi.Security.RateLimiting;

public sealed class RedisRateLimitingMiddleware
{
    private const string FixedWindowScript = @"
local current = redis.call('INCR', KEYS[1])
if current == 1 then
  redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return current
";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Trace
    };

    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<RedisRateLimitingOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RedisRateLimitingMiddleware> _logger;

    public RedisRateLimitingMiddleware(
        RequestDelegate next,
        IServiceProvider serviceProvider,
        IOptions<RedisRateLimitingOptions> options,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<RedisRateLimitingMiddleware> logger)
    {
        _next = next;
        _serviceProvider = serviceProvider;
        _options = options;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var policyName = ResolvePolicyName(context);
        if (string.IsNullOrWhiteSpace(policyName))
        {
            await _next(context);
            return;
        }

        var options = _options.Value;
        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        // Mirrors RateLimitingRegistration's identically-named ASP.NET Core in-memory policies
        // (see its "verify-otp" doc comment): this Redis-backed limiter enforces the exact same
        // policy names independently, off the same [EnableRateLimiting] attribute metadata, so
        // an environment running with both active (e.g. CI's ci.yml, which sets
        // RedisRateLimiting__Enabled=true for the whole test process) gates a request through
        // both -- exempting only one leaves the other still blocking the Staging smoke suite's
        // required call pattern.
        if (StagingTestSupportAuthorization.IsAuthorized(context.Request, _configuration, _environment))
        {
            await _next(context);
            return;
        }

        if (!options.Policies.TryGetValue(policyName, out var policy))
        {
            _logger.LogError(
                "Endpoint requested unknown Redis rate limit policy {PolicyName}.",
                policyName);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Rate limit policy is not configured.",
                status = StatusCodes.Status500InternalServerError
            }, context.RequestAborted);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var window = policy.Window;
        var windowMs = checked((long)window.TotalMilliseconds);
        var bucket = now.ToUnixTimeMilliseconds() / windowMs;
        var retryAfter = TimeSpan.FromMilliseconds(((bucket + 1) * windowMs) - now.ToUnixTimeMilliseconds());
        var partitionKey = RedisRateLimitPartitionKeyResolver.Resolve(context);
        var redisKey = BuildRedisKey(options.InstanceName, policyName, partitionKey, bucket);

        IConnectionMultiplexer redis;

        try
        {
            redis = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis rate limiter could not resolve IConnectionMultiplexer.");

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Rate limiter backend is unavailable.",
                status = StatusCodes.Status503ServiceUnavailable
            }, context.RequestAborted);

            return;
        }

        if (!redis.IsConnected)
        {
            _logger.LogError(
                "Redis rate limiter is enabled but Redis is not connected. Policy={PolicyName}",
                policyName);

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Rate limiter backend is unavailable.",
                status = StatusCodes.Status503ServiceUnavailable
            }, context.RequestAborted);

            return;
        }

        long count;

        try
        {
            var database = redis.GetDatabase();

            count = (long)await database.ScriptEvaluateAsync(
                FixedWindowScript,
                new RedisKey[] { redisKey },
                new RedisValue[] { windowMs + 2_000 });
        }
        catch (RedisException ex)
        {
            _logger.LogError(
                ex,
                "Redis rate limiter failed while evaluating policy {PolicyName}.",
                policyName);

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Rate limiter backend is unavailable.",
                status = StatusCodes.Status503ServiceUnavailable
            }, context.RequestAborted);

            return;
        }

        if (count <= policy.PermitLimit)
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Redis rate limit rejected request. Policy={PolicyName}, Partition={PartitionKey}, Count={Count}, Limit={Limit}",
            policyName,
            PiiMasking.MaskIpsInText(partitionKey),
            count,
            policy.PermitLimit);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers[HeaderNames.RetryAfter] =
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = "Too many requests.",
            status = StatusCodes.Status429TooManyRequests,
            detail = "The request rate limit has been exceeded.",
            policy = policyName,
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
        }, context.RequestAborted);
    }

    private static string? ResolvePolicyName(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
            return null;

        if (endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null)
            return null;

        var rateLimitMetadata = endpoint.Metadata
            .GetOrderedMetadata<EnableRateLimitingAttribute>()
            .LastOrDefault();

        return rateLimitMetadata?.PolicyName;
    }

    private static RedisKey BuildRedisKey(
        string instanceName,
        string policyName,
        string partitionKey,
        long bucket)
    {
        var safeInstance = instanceName.EndsWith(':') ? instanceName : instanceName + ":";
        var safePolicy = Uri.EscapeDataString(policyName);
        var safePartition = Uri.EscapeDataString(partitionKey);

        return $"{safeInstance}rl:{safePolicy}:{safePartition}:{bucket}";
    }
}
