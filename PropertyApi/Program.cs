using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application;
using PropertyApi.Infrastructure;
using PropertyApi.Middleware;
using PropertyApi.Seed;
using System.Security.Claims;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Users.Constants;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Infrastructure.Hubs;
using PropertyApi.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using PropertyApi.Security.Csrf;
using PropertyApi.Security.Headers;
using PropertyApi.Security.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var isTestingOrCi =
    builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
    builder.Environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];

var useRedisRateLimiting =
    !isTestingOrCi &&
    builder.Configuration.GetValue("RateLimiting:Redis:Enabled", true) &&
    !string.IsNullOrWhiteSpace(redisConnectionString);

if (builder.Environment.IsProduction() && !useRedisRateLimiting)
{
    throw new InvalidOperationException(
        "Redis distributed rate limiting is required in Production. Configure ConnectionStrings:Redis or Redis:ConnectionString.");
}
var configuredKnownProxies = ReadKnownProxies(builder.Configuration);
var configuredKnownNetworks = ReadKnownNetworks(builder.Configuration);
var hasConfiguredKnownForwardingSource =
    builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Exists() ||
    builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Exists();

if (builder.Environment.IsProduction() && !hasConfiguredKnownForwardingSource)
{
    throw new InvalidOperationException(
        "ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks is required in Production.");
}

if (builder.Environment.IsProduction() &&
    configuredKnownProxies.Count == 0 &&
    configuredKnownNetworks.Count == 0)
{
    throw new InvalidOperationException(
        "ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks must contain at least one valid IP address or CIDR network in Production.");
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.ForwardLimit = 1;
    options.RequireHeaderSymmetry = true;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var network in configuredKnownNetworks)
    {
        options.KnownNetworks.Add(network);
    }

    foreach (var proxy in configuredKnownProxies)
    {
        options.KnownProxies.Add(proxy);
    }
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});


// -- 1. Culture -----------------------------------------------
// Required for Npgsql decimal/timestamp compatibility
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// -- 2. Application Layer (MediatR + FluentValidation + Behaviors)
builder.Services.AddApplication();

// -- 3. Infrastructure Layer (DB + Identity + Repos + UoW) ----
builder.Services.AddInfrastructure(
    builder.Configuration,
    builder.Environment);

builder.Services.AddPropertyApiSecurityHeaders(builder.Configuration);
builder.Services.AddPropertyApiAntiforgery(builder.Configuration, builder.Environment);

if (useRedisRateLimiting)
{
    builder.Services.AddPropertyApiRedisRateLimiting(
        builder.Configuration,
        builder.Environment);
}

// -- 4. JWT Authentication ------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is missing. Set it via User Secrets in Development " +
        "or as an environment variable in Production.");

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
            IssuerSigningKey = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtKey)),
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
                },

                OnAuthenticationFailed = context =>
                {
                    if (builder.Environment.IsDevelopment())
                    {
                        Console.WriteLine(
                            $"[JWT] Auth failed: {context.Exception.Message}");
                    }

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
                },
            };
    });

builder.Services.AddAuthorization(options =>
{
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
        if (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == "CI")
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            if (allowedOrigins.Length == 0)
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins is required outside Development.");
            }

            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("DevCors", policy =>
//        policy.AllowAnyOrigin()
//              .AllowAnyHeader()
//              .AllowAnyMethod());

//    options.AddPolicy("ProdCors", policy =>
//        policy.WithOrigins(
//                  "https://bizorealestateworld.netlify.app",
//                  "https://www.bizorealestateworld.com")
//              .AllowAnyHeader()
//              .AllowAnyMethod()
//              .AllowCredentials());
//});

// -- 6. Controllers + JSON -------------------------------------
builder.Services
    .AddControllers(options =>
    {
        if (builder.Environment.IsProduction())
        {
            options.Filters.Add(new RequireHttpsAttribute());
        }
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // ReferenceHandler.IgnoreCycles prevents circular nav-property loops
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });



var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

var signalRProvider = builder.Configuration["SignalR:Provider"];

if (string.Equals(signalRProvider, "Redis", StringComparison.OrdinalIgnoreCase))
{
    var signalRRedisConnectionString =
        builder.Configuration.GetConnectionString("SignalRRedis")
        ?? builder.Configuration["SignalR:Redis:ConnectionString"]
        ?? builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(signalRRedisConnectionString))
    {
        throw new InvalidOperationException(
            "SignalR Redis backplane is enabled, but no Redis connection string is configured.");
    }

    signalRBuilder.AddStackExchangeRedis(signalRRedisConnectionString);
}
else if (string.Equals(signalRProvider, "AzureSignalR", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "SignalR:Provider=AzureSignalR is configured, but Azure SignalR registration is not implemented in this build.");
}


// -- 7. Swagger ------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "PropertyApi", Version = "v1" });

    // Allow sending JWT Bearer token from Swagger UI
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token (without 'Bearer ' prefix)"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

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

// --------------------------------------------------------------
var app = builder.Build();


// -- 8. Run DB Migrations --------------------------------------


await IdentitySeeder.SeedRolesAsync(app.Services);

// -- 9. Middleware (order is critical) -------------------------

if (app.Environment.IsProduction())
{
    app.Use(async (context, next) =>
    {
        if (RequestHasForwardedHeaders(context.Request))
        {
            var forwardedHeadersOptions = context.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>()
                .Value;

            if (!IsKnownForwardingSource(
                    context.Connection.RemoteIpAddress,
                    forwardedHeadersOptions))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next();
    });
}

app.UseForwardedHeaders();

if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// MUST be first after forwarded headers: catches all unhandled exceptions before CORS headers are set
// FIX: replaces the inline lambda that had security issues
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UsePropertyApiSecurityHeaders();

// Swagger / OpenAPI
// Enabled by default in Development and CI. For Staging, set Swagger:Enabled=true.
// Do not expose Swagger publicly in Production unless it is protected by network/VPN/auth gateway.
var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Environment.EnvironmentName == "CI"
    || builder.Configuration.GetValue<bool>("Swagger:Enabled");

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

// CORS must run before rate limiting, CSRF, authentication, and authorization.
// Otherwise browser preflight requests may be rejected before CORS headers are added.
app.UseCors("DefaultCors");

if (useRedisRateLimiting)
{
    app.UseRedisRateLimiting();
}
else
{
    app.UseRateLimiter();
}

app.UseCookieCsrfProtection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/notificationHub");

app.Run();

static string GetClientRateLimitPartitionKey(HttpContext httpContext)
{
    return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
}

static IReadOnlyCollection<System.Net.IPAddress> ReadKnownProxies(IConfiguration configuration)
{
    var proxies = new List<System.Net.IPAddress>();

    foreach (var child in configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren())
    {
        var value = child.Value ?? child["Address"] ?? child["IpAddress"];

        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        if (!System.Net.IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies contains an invalid IP address: {value}.");
        }

        proxies.Add(address);
    }

    return proxies;
}

static IReadOnlyCollection<IPNetwork> ReadKnownNetworks(IConfiguration configuration)
{
    var networks = new List<IPNetwork>();

    foreach (var child in configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren())
    {
        var value = child.Value ?? child["Cidr"] ?? child["Network"];
        var prefixLengthValue = child["PrefixLength"];

        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        var cidrParts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var prefixValue = cidrParts[0];

        if (!System.Net.IPAddress.TryParse(prefixValue, out var prefix))
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains an invalid IP network prefix: {value}.");
        }

        int prefixLength;
        if (cidrParts.Length == 2)
        {
            if (!int.TryParse(cidrParts[1], out prefixLength))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains an invalid CIDR prefix length: {value}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(prefixLengthValue))
        {
            if (!int.TryParse(prefixLengthValue, out prefixLength))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains an invalid PrefixLength value: {prefixLengthValue}.");
            }
        }
        else
        {
            prefixLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        }

        networks.Add(new IPNetwork(prefix, prefixLength));
    }

    return networks;
}

static bool RequestHasForwardedHeaders(HttpRequest request)
{
    return request.Headers.ContainsKey("X-Forwarded-For") ||
           request.Headers.ContainsKey("X-Forwarded-Proto") ||
           request.Headers.ContainsKey("X-Forwarded-Host");
}

static bool IsKnownForwardingSource(
    System.Net.IPAddress? remoteIpAddress,
    ForwardedHeadersOptions options)
{
    if (remoteIpAddress is null)
    {
        return false;
    }

    if (options.KnownProxies.Any(proxy => proxy.Equals(remoteIpAddress)))
    {
        return true;
    }

    return options.KnownNetworks.Any(network => network.Contains(remoteIpAddress));
}

public partial class Program
{
}




