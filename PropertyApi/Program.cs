using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application;         
using PropertyApi.Infrastructure;      
using PropertyApi.Middleware;
using PropertyApi.Seed;
using PropertyApi.Infrastructure.Identity.Services;
using System.Security.Claims;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Users.Entities;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

static bool IsNonProductionEnvironment(IHostEnvironment environment)
{
    return environment.IsDevelopment() ||
           environment.EnvironmentName == "Testing" ||
           environment.EnvironmentName == "CI";
}
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

                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<User>>();

                var user = await userManager.FindByIdAsync(userId.ToString());

                if (user is null || user.IsDeleted)
                {
                    context.Fail("The user account is disabled.");
                    return;
                }

                if (!string.Equals(
                        user.SecurityStamp,
                        tokenSecurityStamp,
                        StringComparison.Ordinal))
                {
                    context.Fail("The token is no longer valid.");
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
            }
        };
    });

builder.Services.AddAuthorization();

// -- 5. CORS ---------------------------------------------------
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        if (IsNonProductionEnvironment(builder.Environment))
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
                    "Cors:AllowedOrigins is required outside Development, Testing, and CI.");
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
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // ReferenceHandler.IgnoreCycles prevents circular nav-property loops
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

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
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(
        "contact",
        limiter =>
        {
            limiter.PermitLimit = 5;
            limiter.Window = TimeSpan.FromMinutes(10);
            limiter.QueueLimit = 0;
            limiter.QueueProcessingOrder =
                QueueProcessingOrder.OldestFirst;
        });
});

// --------------------------------------------------------------
var app = builder.Build();
// --------------------------------------------------------------

// -- 8. Run DB Migrations --------------------------------------
await app.MigrateDatabaseAsync(); // Extension method below
await IdentitySeeder.SeedRolesAsync(app.Services);

// -- 9. Middleware (order is critical) -------------------------

// MUST be first: catches all unhandled exceptions before CORS headers are set
// FIX: replaces the inline lambda that had security issues
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger — available in all environments (secured by network in Production)
if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "CI")
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PropertyApi v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
// CORS must be between UseRouting() and UseAuthentication()
app.UseCors("DefaultCors");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program
{
}
// --- Database Migration Helper -------------------------------
static class ApplicationExtensions
{
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var context = services
                .GetRequiredService<PropertyApi.Infrastructure.Persistence.AppDbContext>();
            var logger = services
                .GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Applying database migrations...");
            await context.Database.MigrateAsync();
            logger.LogInformation("Database ready.");
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Migration failed.");

            if (app.Environment.IsDevelopment() ||
                app.Environment.EnvironmentName == "Testing" ||
                app.Environment.EnvironmentName == "CI")
            {
                throw;
            }
        }
    }
}

