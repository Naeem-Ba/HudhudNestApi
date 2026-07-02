using System.Globalization;
using System.Security.Claims;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure;
using PropertyApi.Infrastructure.Hubs;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Persistence.Seeds;
using PropertyApi.Middleware;
using PropertyApi.Security.Csrf;
using PropertyApi.Security.Headers;
using PropertyApi.Security.RateLimiting;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var isTestingOrCi =
    builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
    builder.Environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

var redisConnectionString =
    builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration["RedisRateLimiting:ConnectionString"]
    ?? builder.Configuration["REDIS_CONNECTION_STRING"]
    ?? builder.Configuration["REDIS_URL"];

var redisRateLimitingEnabled =
    builder.Configuration.GetValue<bool?>("RateLimiting:Redis:Enabled")
    ?? builder.Configuration.GetValue<bool?>("RedisRateLimiting:Enabled")
    ?? builder.Environment.IsProduction();

var useRedisRateLimiting =
    redisRateLimitingEnabled &&
    !string.IsNullOrWhiteSpace(redisConnectionString);

if (builder.Environment.IsProduction() && !useRedisRateLimiting)
{
    throw new InvalidOperationException(
        "Redis distributed rate limiting is required in Production. Configure ConnectionStrings:Redis, Redis:ConnectionString, RedisRateLimiting:ConnectionString, REDIS_CONNECTION_STRING, or REDIS_URL.");
}



builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var forwardedHeadersSection = builder.Configuration.GetSection("ForwardedHeaders");

    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    // Some managed proxies send non-symmetric forwarded headers. Keep this
    // relaxed, but only trust configured proxy IPs/networks by default.
    options.RequireHeaderSymmetry = false;
    options.ForwardLimit = forwardedHeadersSection.GetValue<int?>("ForwardLimit") ?? 1;

    var trustAllProxies = forwardedHeadersSection.GetValue<bool>("TrustAllProxies");

    if (trustAllProxies && builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:TrustAllProxies must not be enabled in Production. Configure KnownProxies or KnownNetworks instead.");
    }

    if (trustAllProxies)
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }

    foreach (var proxy in forwardedHeadersSection.GetSection("KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var network in forwardedHeadersSection.GetSection("KnownNetworks").Get<string[]>() ?? [])
    {
        if (TryParseCidr(network, out var ipNetwork))
        {
            options.KnownNetworks.Add(ipNetwork);
        }
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

// -- 2. Application Layer --------------------------------------
builder.Services.AddApplication();

// -- 3. Infrastructure Layer -----------------------------------
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

// -- 4. JWT Authentication -------------------------------------
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
                    Console.WriteLine($"[JWT] Auth failed: {context.Exception.Message}");
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
        if (builder.Environment.IsDevelopment() ||
            builder.Environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase))
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

            // ✅ إصلاح H-2: إضافة AllowCredentials() لـ Production CORS.
            //
            // المشكلة القديمة: كان AllowCredentials() مفقوداً في Production،
            // مما كان يُسبِّب فشل SignalR long-polling وSSE في المتصفح.
            //
            // لماذا يحتاج SignalR لـ AllowCredentials؟
            //   SignalR يُرسل Authorization header في كل طلب.
            //   المتصفح يرفض إرسال Credentials إلى Origin غير مُصرَّح به.
            //   بدون AllowCredentials() → المتصفح يمنع الطلب → الإشعارات الفورية تفشل.
            //
            // ⚠️ قاعدة مهمة: AllowCredentials() لا تعمل مع AllowAnyOrigin().
            //   يجب دائماً استخدامها مع WithOrigins() محدد — وهو ما نفعله هنا.
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials(); // ✅ مطلوب لـ SignalR + Cookies
        }
    });
});

// -- 5.5 Output Cache (Redis-backed) ──────────────────────────
// ✅ إصلاح M-2: استبدال ResponseCache المحلي بـ OutputCache الموزَّع.
//
// المشكلة القديمة: [ResponseCache(Duration=300)] يخزن في ذاكرة Server واحد.
// عند تشغيل عدة Instances في Production، كل Instance يُنفِّذ الـ Query المكلفة.
//
// الإصلاح: OutputCache يدعم Redis → الـ Cache مشترك بين كل Instances.
//
// التعليم:
//   ResponseCache = يحفظ في RAM للـ Server الحالي فقط ❌
//   OutputCache   = يحفظ في Redis مشترك بين كل الـ Servers ✅
builder.Services.AddOutputCache(options =>
{
    // سياسة "market-insights": cache لمدة 5 دقائق + يمكن إبطالها بـ Tag
    options.AddPolicy("market-insights", policy =>
        policy.Expire(TimeSpan.FromMinutes(5))
              .Tag("analytics")    // يمكن إبطال الـ cache بـ tag عند تحديث البيانات
              .SetVaryByQuery("*")); // يحفظ نسخة مختلفة لكل countryCode مختلف
});

// -- 6. Controllers + JSON -------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// -- 7. SignalR ------------------------------------------------
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

// ✅ فرض backplane في الإنتاج — بنفس نمط useRedisRateLimiting أعلاه (سطر 50-54).
// بدون هذا الفحص، نسيان الإعداد يؤدي لفقدان صامت للإشعارات بين instances مختلفة
// خلف load balancer، دون أي خطأ عند الإقلاع.
if (builder.Environment.IsProduction() &&
    !string.Equals(signalRProvider, "Redis", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(signalRProvider, "AzureSignalR", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "SignalR requires a distributed backplane in Production (SignalR:Provider=Redis or AzureSignalR). " +
        "Without it, real-time notifications will silently fail to reach users connected to a different instance.");
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

// -- 10. Database startup ---------------------------------------
// Apply pending EF Core migrations before any seed code runs.
// Without this, seed queries can fail when newly added tables do not exist yet.
await ApplyPendingMigrationsAsync(app.Services, app.Configuration, app.Environment);

// This seed is protected by PostgreSQL advisory transaction lock to prevent
// duplicate inserts when multiple instances start in parallel.
await SeedStartupDataAsync(app.Services, app.Configuration);

// -- 11. Middleware order --------------------------------------
app.UseForwardedHeaders();

if (app.Environment.IsProduction())
{
    app.UseHsts();
}
else
{
    app.UseHttpsRedirection();
}

// Must be early: catches unhandled exceptions before downstream middleware.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UsePropertyApiSecurityHeaders();

var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase)
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

// ✅ إصلاح M-2 (جزء 2): تفعيل OutputCache في الـ Pipeline.
// يجب أن يأتي بعد UseCors وقبل UseAuthentication.
app.UseOutputCache();

app.UseAuthentication();

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

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/notificationHub");

app.Run();


static async Task ApplyPendingMigrationsAsync(
    IServiceProvider services,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    var shouldApplyMigrations =
        configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
        ?? !environment.IsProduction();

    if (!shouldApplyMigrations)
        return;

    using var scope = services.CreateScope();

    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseMigration");

    var providerName = context.Database.ProviderName ?? string.Empty;
    var isPostgres = providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    if (!isPostgres)
    {
        logger.LogInformation(
            "Skipping startup migrations because database provider is {ProviderName}.",
            providerName);

        return;
    }

    var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();

    if (pendingMigrations.Length == 0)
    {
        logger.LogInformation("No pending EF Core migrations found.");
        return;
    }

    logger.LogInformation(
        "Applying {Count} pending EF Core migration(s): {Migrations}",
        pendingMigrations.Length,
        string.Join(", ", pendingMigrations));

    await context.Database.MigrateAsync();

    logger.LogInformation("EF Core database migrations completed successfully.");
}

static async Task SeedStartupDataAsync(
    IServiceProvider services,
    IConfiguration configuration)
{
    var shouldSeed = configuration.GetValue("Database:SeedOnStartup", true);

    if (!shouldSeed)
        return;

    using var scope = services.CreateScope();

    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("StartupSeed");

    var providerName = context.Database.ProviderName ?? string.Empty;
    var isPostgres = providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    if (isPostgres)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(20260621194421::bigint);");

            logger.LogInformation("Startup seed lock acquired.");

            await CurrencySeed.SeedAsync(context);
            await GovernoratesSeed.SeedAsync(context);
            await PropertyTypesSeed.SeedAsync(context);
            await ApplicationRolesSeed.SeedAsync(roleManager);

            await transaction.CommitAsync();

            logger.LogInformation("Startup seed completed.");
        });

        return;
    }

    await CurrencySeed.SeedAsync(context);
    await GovernoratesSeed.SeedAsync(context);
    await PropertyTypesSeed.SeedAsync(context);
    await ApplicationRolesSeed.SeedAsync(roleManager);
}

// ✅ إصلاح M-3: تقسيم Rate Limiting باستخدام UserId للمستخدمين المسجلين.
//
// المشكلة القديمة: كان يعتمد على IP فقط، مما يسمح للمستخدم الخبيث
// بتجاوز الحدود باستخدام VPN أو IPs مختلفة.
//
// الإصلاح:
//   - المستخدم المسجل  → partition بـ "user:{userId}"  (أكثر دقة وأمناً)
//   - المستخدم الزائر  → partition بـ "ip:{ipAddress}"  (كما كان)
//
// التعليم: لماذا هذا أفضل؟
//   الـ IP يمكن تغييره بسهولة (VPN). لكن الـ UserId ثابت في الـ Token.
//   حتى لو غيّر المهاجم IP، نفس الـ UserId سيبقى محدود.
static string GetClientRateLimitPartitionKey(HttpContext httpContext)
{
    // نقرأ UserId من الـ JWT Token إذا كان المستخدم مسجلاً
    var userId = httpContext.User?
        .FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

    // المستخدم المسجل: نستخدم UserId كـ partition key
    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    // الزائر غير المسجل: نستخدم IP كـ partition key (كما كان)
    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
    return $"ip:{ip}";
}

static bool TryParseCidr(
    string value,
    out Microsoft.AspNetCore.HttpOverrides.IPNetwork network)
{
    network = default!;

    var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
    if (parts.Length != 2 ||
        !IPAddress.TryParse(parts[0], out var address) ||
        !int.TryParse(parts[1], out var prefixLength))
    {
        return false;
    }

    try
    {
        network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefixLength);
        return true;
    }
    catch (ArgumentOutOfRangeException)
    {
        return false;
    }
}



public partial class Program
{
}