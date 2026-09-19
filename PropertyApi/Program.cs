using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using PropertyApi.Application;
using PropertyApi.Infrastructure;
using PropertyApi.Infrastructure.Hubs;
using PropertyApi.Middleware;
using PropertyApi.Observability;
using PropertyApi.Security.Csrf;
using PropertyApi.Security.Headers;
using PropertyApi.Security.RateLimiting;
using PropertyApi.Performance;
using PropertyApi.Configuration;
using PropertyApi.Health;

var builder = WebApplication.CreateBuilder(args);

builder.AddPropertyApiObservability();
StagingEnvironmentGuard.Validate(builder.Configuration, builder.Environment);

builder.Services.AddTrustedForwardedHeaders(
    builder.Configuration,
    builder.Environment);

var isTestingOrCi =
    builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
    builder.Environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

var (redisConnectionString, hasRedisConnectionString) = RedisConnectionResolver.ResolveAndConfigure(builder);

var redisRateLimitingEnabled =
    builder.Configuration.GetValue<bool?>("RateLimiting:Redis:Enabled")
    ?? builder.Configuration.GetValue<bool?>("RedisRateLimiting:Enabled")
    ?? builder.Environment.IsProduction();

var useRedisRateLimiting = redisRateLimitingEnabled && hasRedisConnectionString;

// General-purpose, always-available Redis connection -- independent of Redis rate limiting
// (RedisRateLimitingServiceCollectionExtensions.TryAddSingleton reuses this same
// registration when rate limiting is also enabled, rather than opening a second
// connection). Registered whenever a Redis connection string exists, in every environment,
// not just Production: OpenTelemetry's AddRedisInstrumentation() call below only produces
// Redis spans for IConnectionMultiplexer instances it can discover via DI, and
// ObservabilitySyntheticController's dependency check needs a real Redis ping it can trace.
if (hasRedisConnectionString)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Redis");
        var options = RedisConfigurationOptionsFactory.Build(redisConnectionString!);
        var multiplexer = ConnectionMultiplexer.Connect(options);

        logger.LogInformation(
            "Redis connection initialized. IsConnected={IsConnected}, Endpoints={Endpoints}",
            multiplexer.IsConnected,
            string.Join(",", multiplexer.GetEndPoints().Select(e => e.ToString())));

        return multiplexer;
    });
}

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// -- 1. Culture ------------------------------------------------
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// -- 2. Application Layer --------------------------------------
builder.Services.AddApplication();

// -- 3. Infrastructure Layer -----------------------------------
builder.Services.AddInfrastructure(
    builder.Configuration,
    builder.Environment);

if (builder.Environment.IsStaging() &&
    builder.Configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
{
    builder.Services.AddHttpClient("ObservabilitySynthetic", client =>
    {
        var endpoint = builder.Configuration["Observability:Synthetic:HttpEndpoint"]
            ?? "http://synthetic-http:5678/";
        client.BaseAddress = new Uri(endpoint);
        client.Timeout = TimeSpan.FromSeconds(5);
    });
}

builder.Services.AddScaleOutOutputCaching(
    builder.Configuration,
    builder.Environment);

// Force the application cache to use the same Redis value resolved above.
// This prevents CachedSecurityStampValidator from using localhost:6379 in Production.
if (hasRedisConnectionString)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "PropertyApi:";
    });
}
else
{
    // Development/Testing fallback only. Production is guarded above.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddPropertyApiSecurityHeaders(builder.Configuration);
builder.Services.AddPropertyApiAntiforgery(builder.Configuration, builder.Environment);

if (useRedisRateLimiting)
{
    builder.Services.AddPropertyApiRedisRateLimiting(
        builder.Configuration,
        builder.Environment);
}

// -- 4. JWT Authentication ---------------------------------------
builder.Services.AddPropertyApiJwtAuthentication(builder.Configuration, builder.Environment);

// -- 5. CORS -------------------------------------------------------
builder.Services.AddPropertyApiCors(builder.Configuration, builder.Environment, isTestingOrCi);

// -- 6. Controllers + JSON -------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // RELEASE-BLOCKERS-AR.md B-11: was `null` (PascalCase by default), forcing every
        // endpoint that needed camelCase — historically just /auth/login — to carry manual
        // [JsonPropertyName] attributes, while the Angular client ran a client-side
        // apiToCamelCase() adapter across ~15 other files to paper over the rest. One
        // consistent policy for the whole API removes both: LoginResponseDto's attributes
        // are now redundant (kept for clarity, not correctness) and the adapter is deleted.
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// -- 7. SignalR ------------------------------------------------
builder.Services.AddPropertyApiSignalR(
    builder.Configuration,
    builder.Environment,
    redisConnectionString,
    hasRedisConnectionString);

// -- 8. Swagger ------------------------------------------------
builder.Services.AddPropertyApiSwagger();

// -- 9. Rate Limiting ------------------------------------------
builder.Services.AddPropertyApiRateLimiting();

var app = builder.Build();

// The IConnectionMultiplexer singleton is otherwise built lazily by whichever request needs it
// first, and with abortConnect=false ConnectionMultiplexer.Connect returns before the socket is
// up. On a freshly started instance the first rate-limited requests therefore hit
// IsConnected=false and were rejected with the limiter's intentional fail-closed 503
// ("Redis rate limiter is enabled but Redis is not connected"), while /health/ready already
// answered 200 -- seen as 10 unexpected 5xx in the rate-limit-login gate and, equally, a
// window of failed logins after every deploy. Connect eagerly before Kestrel starts listening
// and give the connection a bounded time to establish. If Redis is really down the instance
// still starts (fail-closed behaviour and readiness are unchanged).
if (useRedisRateLimiting)
{
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Redis");
    var redisMultiplexer = app.Services.GetRequiredService<IConnectionMultiplexer>();
    var connectBudgetSeconds = Math.Clamp(
        app.Configuration.GetValue<int?>("Redis:StartupConnectTimeoutSeconds") ?? 15, 0, 60);
    var connectDeadline = DateTime.UtcNow.AddSeconds(connectBudgetSeconds);
    while (!redisMultiplexer.IsConnected && DateTime.UtcNow < connectDeadline)
    {
        await Task.Delay(100);
    }

    if (redisMultiplexer.IsConnected)
    {
        startupLogger.LogInformation("Redis connected before accepting traffic.");
    }
    else
    {
        startupLogger.LogWarning(
            "Redis was not connected after {Seconds}s; starting anyway. Rate-limited endpoints will answer 503 until it connects.",
            connectBudgetSeconds);
    }
}

// -- 10. Reference-data seeding ---------------------------------
await app.SeedReferenceDataAsync();

// -- 11. Middleware order --------------------------------------
app.UseForwardedHeaders();

if (app.Environment.IsProduction() || app.Environment.IsStaging())
{
    // Production incident (2026-09-18): Request.IsHttps is still false on every request here
    // even after ForwardedHeadersRegistration.cs's own RequireHeaderSymmetry=false fix (that
    // fix addressed a header-count-mismatch log/HSTS issue, not this) -- confirmed live via a
    // direct curl to https://wohnungen-api.onrender.com: cookies whose Secure attribute is
    // computed from Request.IsHttps (CookieSecurePolicy.SameAsRequest in CsrfExtensions.cs)
    // came back without it, and every browser drops a SameSite=None cookie missing Secure
    // outright. Render sits behind Cloudflare; whatever the true internal cause is (an
    // internal hop that isn't itself HTTPS, X-Forwarded-Proto not propagating as expected,
    // or something else), it isn't fixable from inside this app's ForwardedHeaders config --
    // confirmed by testing that config change already, live, twice.
    //
    // This sidesteps the unreliable detection instead of continuing to chase it: Production is
    // only ever reachable from the outside over real HTTPS (Cloudflare enforces this at its own
    // edge before any request reaches Render at all), so forcing the scheme here is not
    // pretending — it's asserting a fact that is already true for every real client, and letting
    // the rest of the pipeline (HSTS, CookieSecurePolicy.SameAsRequest, anything else that reads
    // Request.IsHttps) work correctly on that fact instead of on an unreliable internal signal.
    // Must run immediately after UseForwardedHeaders() so it wins over whatever that middleware
    // concluded, and before anything else in the pipeline reads Request.IsHttps.
    //
    // Extended to Staging (2026-09-18, same day, found live via a Playwright E2E run — see
    // docs/audit/CURRENT-ISSUES-VERIFICATION.md): Staging has the exact same structural cause,
    // arguably more reliably so -- its `propertyapi-staging-api.onrender.com` origin is served
    // directly off Render's own TLS-terminating edge (no custom domain/Cloudflare in front of
    // it the way Production has), and `ForwardedHeaders__Enabled=false` there (see
    // render-staging-deployment memory: no documented Render proxy CIDR was available to scope
    // KnownNetworks to) means Kestrel never learns the original request was HTTPS at all.
    // Confirmed live: a real login + forced-401 + silent-refresh E2E run against deployed
    // Staging got a 403 (CSRF validation failed) from POST /auth/refresh, because the
    // antiforgery cookie's Secure attribute -- computed from this same unreliable
    // Request.IsHttps -- was never set, so the browser dropped the SameSite=None cookie and no
    // real CSRF token ever reached the client to echo back.
    app.Use((context, next) =>
    {
        context.Request.Scheme = "https";
        return next();
    });
}

app.UsePerformanceInstanceHeader(app.Environment, app.Configuration);
app.UsePerformanceDatabaseDiagnostics(app.Environment, app.Configuration);
app.UsePropertyApiObservability();

if (app.Environment.IsProduction())
{
    app.UseHsts();
}
else
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UsePropertyApiSecurityHeaders();

var swaggerEnabled = app.Environment.IsDevelopment()
    || isTestingOrCi
    || (!app.Environment.IsProduction() && builder.Configuration.GetValue<bool>("Swagger:Enabled"));

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PropertyApi v1");
        c.RoutePrefix = "swagger";
        c.DisplayRequestDuration();
    });
}

app.UseStaticFiles();

app.UseRouting();

app.UseCors("DefaultCors");

app.UseAuthentication();
app.UseMiddleware<PropertyApi.Security.PhoneVerificationRestrictionMiddleware>();

if (useRedisRateLimiting)
{
    app.UseRedisRateLimiting();
}
else
{
    app.UseRateLimiter();
}

app.UseCookieCsrfProtection();

app.UseAuthorization();

app.UseOutputCache();
app.MapOperationalHealthEndpoints();
app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");

app.Run();

public partial class Program
{
}
