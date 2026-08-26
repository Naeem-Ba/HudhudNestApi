using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Users.Constants;
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
using PropertyApi.Infrastructure.Persistence.Seeds;

var builder = WebApplication.CreateBuilder(args);

builder.AddPropertyApiObservability();
StagingEnvironmentGuard.Validate(builder.Configuration, builder.Environment);

builder.Services.AddTrustedForwardedHeaders(
    builder.Configuration,
    builder.Environment);

var isTestingOrCi =
    builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
    builder.Environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

var rawRedisConnectionString = GetRedisConnectionString(builder.Configuration);
var redisConnectionString = NormalizeRedisConnectionString(rawRedisConnectionString);
var hasRedisConnectionString = !string.IsNullOrWhiteSpace(redisConnectionString);

if (hasRedisConnectionString)
{
    // Normalize all Redis-related keys so Infrastructure, RateLimiting, SignalR,
    // and IDistributedCache read the same valid value instead of falling back to localhost:6379.
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Redis"] = redisConnectionString,
        ["Redis:ConnectionString"] = redisConnectionString,
        ["RedisRateLimiting:ConnectionString"] = redisConnectionString,
        ["SignalR:Redis:ConnectionString"] = redisConnectionString
    });
}

var redisRateLimitingEnabled =
    builder.Configuration.GetValue<bool?>("RateLimiting:Redis:Enabled")
    ?? builder.Configuration.GetValue<bool?>("RedisRateLimiting:Enabled")
    ?? builder.Environment.IsProduction();

var useRedisRateLimiting = redisRateLimitingEnabled && hasRedisConnectionString;

if (builder.Environment.IsProduction() && !hasRedisConnectionString)
{
    throw new InvalidOperationException(
        "Redis is required in Production. Configure ConnectionStrings:Redis, Redis:ConnectionString, RedisRateLimiting:ConnectionString, REDIS_CONNECTION_STRING, or REDIS_URL.");
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

// -- 4. JWT Authentication -------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is missing. Set it via User Secrets in Development or as an environment variable in Production.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdText = context.Principal?
                    .FindFirstValue(ClaimTypes.NameIdentifier);

                var tokenSecurityStamp = context.Principal?
                    .FindFirstValue(CustomClaimTypes.SecurityStamp);

                if (!Guid.TryParse(userIdText, out var userId) ||
                    string.IsNullOrWhiteSpace(tokenSecurityStamp))
                {
                    context.Fail("The token does not contain valid user data.");
                    return;
                }

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtSecurityStampValidation");

                try
                {
                    var securityStampValidator = context.HttpContext.RequestServices
                        .GetRequiredService<IUserSecurityStampValidator>();

                    var validationResult = await securityStampValidator.ValidateAsync(
                        userId,
                        tokenSecurityStamp,
                        context.HttpContext.RequestAborted);

                    if (!validationResult.IsValid)
                    {
                        context.Fail(validationResult.FailureMessage ?? "The token is no longer valid.");
                    }
                }
                catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Do not let authentication infrastructure convert Redis/cache failures into HTTP 500.
                    // The validator itself should fall back to DB; this is the final safety net.
                    logger.LogError(ex, "Security stamp validation failed during JWT authentication.");
                    context.Fail("Token validation failed.");
                }
            },

            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuthentication");

                logger.LogWarning(context.Exception, "JWT authentication failed.");
                return Task.CompletedTask;
            },

            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    path.StartsWithSegments("/notificationHub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // SECURITY FIX: default-deny.
    //
    // Without a fallback policy, ASP.NET Core leaves an endpoint that carries no
    // [Authorize] and no [AllowAnonymous] open to anonymous callers. Being public was
    // therefore the result of forgetting an attribute, not of deciding anything --
    // one missed attribute in a future review would silently publish an endpoint, and
    // nothing in the build or the test suite would notice.
    //
    // With this policy the default is reversed: an unannotated endpoint returns 401,
    // and every public endpoint has to say [AllowAnonymous] out loud. All 107 existing
    // endpoints were audited before this was switched on; the 13 that were public by
    // omission now carry the attribute explicitly, and PublicEndpointPolicyTests fails
    // the build if a new endpoint is added without an explicit decision either way.
    //
    // Note this only governs endpoint routing. Health checks call .AllowAnonymous()
    // themselves, and the Swagger middleware runs before UseAuthorization, so neither
    // is affected.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy(RoleNames.Agent, policy =>
        policy.RequireRole(RoleNames.Agent));
});

// -- 5. CORS ---------------------------------------------------
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        // RELEASE-BLOCKERS-AR.md B-13: the refresh-token cookie needs a credentialed CORS
        // policy to ever reach the browser — AllowAnyOrigin() and AllowCredentials() are
        // mutually exclusive by the CORS spec itself (browsers reject the combination
        // outright), so every environment with a configured origin list now gets the
        // credentialed policy, not just Production. This is not new risk: Development's
        // appsettings.Development.example.json and Testing's appsettings.Testing.json both
        // already list the real frontend origin(s).
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment() || isTestingOrCi)
        {
            // No origins configured at all (e.g. a fresh clone before appsettings.Development
            // is filled in) — permissive fallback so the API still starts. Note this
            // combination cannot carry the refresh-token cookie; see RefreshTokenCookie.
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            throw new InvalidOperationException("Cors:AllowedOrigins is required outside Development.");
        }
    });
});


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
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

var signalRProvider = builder.Configuration["SignalR:Provider"];
var requireSignalRBackplane = builder.Configuration.GetValue<bool?>("SignalR:RequireBackplane") ?? false;

if (string.Equals(signalRProvider, "Redis", StringComparison.OrdinalIgnoreCase))
{
    if (!hasRedisConnectionString)
    {
        throw new InvalidOperationException(
            "SignalR Redis backplane is enabled, but no Redis connection string is configured.");
    }

    signalRBuilder.AddStackExchangeRedis(redisConnectionString!);
}
else if (string.Equals(signalRProvider, "AzureSignalR", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "SignalR:Provider=AzureSignalR is configured, but Azure SignalR registration is not implemented in this build.");
}
else if (builder.Environment.IsProduction() && requireSignalRBackplane)
{
    throw new InvalidOperationException(
        "SignalR backplane is required because SignalR:RequireBackplane=true. Configure SignalR:Provider=Redis or AzureSignalR.");
}

// -- 8. Swagger ------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "PropertyApi", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token without the 'Bearer ' prefix."
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// -- 9. Rate Limiting ------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("send-otp", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("verify-otp", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-password-reset", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("contact", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-logout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("public-search", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("geo-search", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // RELEASE-BLOCKERS-AR.md B-7: GET /api/agencies/{slug} was anonymous with no rate
    // limit at all. Same cadence as public-search.
    options.AddPolicy("agencies-public", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("visits", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("reviews", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(24),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// -- 10b. Reference-data seeding ---------------------------------
// BUG FIX: DatabaseSeeder.SeedAsync existed in the codebase (governorates,
// districts, neighborhoods, property types, currencies, roles) but was never
// actually called from anywhere — dead code, which is the reason those lookup
// tables were empty in practice. All the individual seed methods are
// idempotent ("ensure exists" per row), so running this on every startup is
// safe and cheap; it must NOT block the app from serving requests if it fails
// (e.g. DB not migrated yet on first deploy), so failures are logged, not
// thrown.
using (var seedScope = app.Services.CreateScope())
{
    try
    {
        await DatabaseSeeder.SeedAsync(seedScope.ServiceProvider);
    }
    catch (Exception ex)
    {
        seedScope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseSeeder")
            .LogError(ex, "Reference-data seeding failed at startup — lookup tables " +
                "(governorates/districts/neighborhoods/property types) may be incomplete " +
                "until this is resolved and the app restarts.");
    }
}

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

static string? GetRedisConnectionString(IConfiguration configuration)
{
    return configuration.GetConnectionString("Redis")
        ?? configuration["Redis:ConnectionString"]
        ?? configuration["RedisRateLimiting:ConnectionString"]
        ?? configuration["REDIS_CONNECTION_STRING"]
        ?? configuration["REDIS_URL"];
}

static string? NormalizeRedisConnectionString(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var trimmed = value.Trim();

    if (!trimmed.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
        !trimmed.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
    {
        return trimmed;
    }

    if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
    {
        return trimmed;
    }

    var parts = new List<string>
    {
        $"{uri.Host}:{uri.Port}",
        "abortConnect=false",
        "connectRetry=3",
        "connectTimeout=5000",
        "syncTimeout=5000"
    };

    if (!string.IsNullOrWhiteSpace(uri.UserInfo))
    {
        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        var separatorIndex = userInfo.IndexOf(':');
        var password = separatorIndex >= 0
            ? userInfo[(separatorIndex + 1)..]
            : userInfo;

        if (!string.IsNullOrWhiteSpace(password))
        {
            parts.Add($"password={password}");
        }
    }

    if (uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase))
    {
        parts.Add("ssl=true");
    }

    var databaseText = uri.AbsolutePath.Trim('/');
    if (int.TryParse(databaseText, out var database))
    {
        parts.Add($"defaultDatabase={database}");
    }

    return string.Join(',', parts);
}

static string GetClientRateLimitPartitionKey(HttpContext httpContext)
{
    var userId = httpContext.User?
        .FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
    return $"ip:{ip}";
}


public partial class Program
{
}
