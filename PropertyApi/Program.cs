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

// -- 10. Reference-data seeding ---------------------------------
await app.SeedReferenceDataAsync();

// -- 11. Middleware order --------------------------------------
app.UseForwardedHeaders();
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
