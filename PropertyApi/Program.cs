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


var builder = WebApplication.CreateBuilder(args);

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
            OnAuthenticationFailed = ctx =>
            {
                // Log only in Development — avoid leaking details in Production
                if (builder.Environment.IsDevelopment())
                    Console.WriteLine($"[JWT] Auth failed: {ctx.Exception.Message}");
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

// --------------------------------------------------------------
var app = builder.Build();
// --------------------------------------------------------------

// -- 8. Run DB Migrations --------------------------------------
await app.MigrateDatabaseAsync(); // Extension method below

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

// CORS must be between UseRouting() and UseAuthentication()
var corsPolicy = app.Environment.IsDevelopment() ? "DevCors" : "ProdCors";
app.UseCors(corsPolicy);

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

            // In Development: crash immediately so you see the problem
            // In Production: log and continue (Render will restart the service)
            if (app.Environment.IsDevelopment()) throw;
        }
    }
}


//using Microsoft.AspNetCore.Authentication.JwtBearer;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.IdentityModel.Tokens;
//using Microsoft.EntityFrameworkCore;
//using System.Text;
//using PropertyApi.Data;
//using PropertyApi.Models;
//using PropertyApi.Services;
//using System.Globalization;
//using System.Text.Json.Serialization;
//using System.Security.Claims;
////using PropertyApi.Infrastructure.Persistence;
////using PropertyApi.Domain.Users.Entities;

//var builder = WebApplication.CreateBuilder(args);

//// ============================
//// 1 ??????? ?????? (Culture)
//// ============================
//// 1. ??????? ??????? ????? ????? ??????? ??????? (??? Latitude/Longitude)
//AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
//CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
//CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

//// ============================
//// 2 ????? ???????? (Database)
//// ============================
//// 2. ????? ?????? PostgreSQL ?? ??? ??????? ?? ???? Render
//builder.Services.AddDbContext<WohnungenContext>(options =>
//{
//    string? connUrl;

//    // ?? Production? ???? ?? DATABASE_URL (Render)
//    if (builder.Environment.IsProduction())
//    {
//        connUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

//        if (string.IsNullOrEmpty(connUrl))
//        {
//            throw new Exception("DATABASE_URL environment variable not found in Production!");
//        }

//        // ????? ???? Render ??? Connection String
//        if (connUrl.StartsWith("postgres://") || connUrl.StartsWith("postgresql://"))
//        {
//            var uri = new Uri(connUrl);
//            var db = uri.AbsolutePath.TrimStart('/');
//            var user = uri.UserInfo.Split(':')[0];
//            var passwd = uri.UserInfo.Split(':')[1];
//            var port = uri.Port > 0 ? uri.Port : 5432;

//            connUrl = $"Host={uri.Host};Port={port};Database={db};Username={user};Password={passwd};SSL Mode=Require;Trust Server Certificate=true;";
//        }
//    }
//    else
//    {
//        // ?? Development? ???? ?? appsettings.json
//        connUrl = builder.Configuration.GetConnectionString("DefaultConnection");

//        if (string.IsNullOrWhiteSpace(connUrl))
//        {
//            throw new Exception("Connection string 'DefaultConnection' not found in appsettings.json!");
//        }
//    }

//    options.UseNpgsql(connUrl);

//    // Logging (???????)
//    if (builder.Environment.IsDevelopment())
//    {
//        options.EnableSensitiveDataLogging();
//        options.EnableDetailedErrors();
//    }
//});

//Console.WriteLine($"[INFO] Environment: {builder.Environment.EnvironmentName}");

//// ============================
//// Identity
//// ============================

////builder.Services.AddIdentity<User, IdentityRole<Guid>>(options =>
////{
////    options.Password.RequireDigit = true;
////    options.Password.RequiredLength = 8;
////    options.Password.RequireNonAlphanumeric = false;
////    options.User.RequireUniqueEmail = true;
////})
////.AddEntityFrameworkStores<AppDbContext>()
////.AddDefaultTokenProviders();

//builder.Services.AddIdentity<Benutzer, IdentityRole<int>>(options =>
//{
//    options.Password.RequireDigit = true;
//    options.Password.RequiredLength = 8;
//    options.Password.RequireNonAlphanumeric = false;
//    options.User.RequireUniqueEmail = true;
//})
//.AddEntityFrameworkStores<WohnungenContext>()
//.AddDefaultTokenProviders();

//// ============================
//// 3 ??????? CORS (??? ????)
//// ============================
//// 3. ????? ??????? ???? Cors
//builder.Services.AddCors(options =>
//{
//    // Policy ??? Development
//    options.AddPolicy("DevCors", policy =>
//        policy.AllowAnyOrigin()
//              .AllowAnyHeader()
//              .AllowAnyMethod());

//    // Policy ??? Production
//    options.AddPolicy("ProdCors", policy =>
//        policy.WithOrigins(
//                  "https://bizorealestateworld.netlify.app",
//                  "https://www.bizorealestateworld.com"  
//               )
//              .AllowAnyHeader()
//              .AllowAnyMethod()
//              .AllowCredentials());
//});

//// ============================
//// 4? ??????? Controllers ? JSON
//// ============================
//// 4. ??????? Cloudinary

//builder.Services.AddControllers().AddJsonOptions(options => {
//    options.JsonSerializerOptions.PropertyNamingPolicy = null;
//    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
//    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
//});


//// ============================
//// 5?? ??????? JWT
//// ============================

//builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

//builder.Services.AddAuthentication(options =>
//{
//    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
//    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
//})
//.AddJwtBearer(options =>
//{
//    var jwt = builder.Configuration.GetSection("Jwt");

//    options.RequireHttpsMetadata = true; // Render ???? HTTPS
//    options.SaveToken = true;

//    options.TokenValidationParameters = new TokenValidationParameters
//    {
//        ValidateIssuer = true,
//        ValidateAudience = true,
//        ValidateLifetime = true,
//        ValidateIssuerSigningKey = true,
//        ValidIssuer = jwt["Issuer"],
//        ValidAudience = jwt["Audience"],
//        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
//        ClockSkew = TimeSpan.FromSeconds(30)
//    };

//    options.Events = new JwtBearerEvents
//    {
//        OnAuthenticationFailed = ctx =>
//        {
//            Console.WriteLine("JWT AUTH FAILED: " + ctx.Exception.Message);
//            return Task.CompletedTask;
//        }
//    };
//});

////????? PhotoService ??????
//if (!string.IsNullOrEmpty(builder.Configuration["CLOUDINARY_URL"]))
//{
//    builder.Services.AddScoped<IPhotoService, PhotoService>();// PhotoService ??? ??? ????? ?????? ?????
//}
//// ============================
//// 6? ????? ????
//// ============================
//builder.Services.AddAuthorization();
//builder.Services.AddScoped<JwtService>();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
//builder.Services.AddScoped<IWohnungService, WohnungService>();
//// ============================
//// 7? ???? ???????
//// ============================
//var app = builder.Build();

//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider
//        .GetRequiredService<WohnungenContext>();

//    try
//    {
//        db.Database.CanConnect();

//        Console.WriteLine("Database connected successfully");
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"Database connection failed: {ex.Message}");
//    }
//}
////============================= ?????? ???? Exceptions ??? Production
//app.UseExceptionHandler(errorApp =>
//{
//    errorApp.Run(async context =>
//    {
//        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
//        var ex = feature?.Error;

//        context.Response.ContentType = "application/json";
//        // ????? Headers ??? CORS ?????? ??? ????? ???? ????? ????? ???????
//        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
//    if (app.Environment.IsDevelopment())
//    {
//        var result = System.Text.Json.JsonSerializer.Serialize(new
//        {
//            message = ex?.Message,
//            stackTrace = ex?.StackTrace,
//            innerException = ex?.InnerException?.Message
//        });

//        await context.Response.WriteAsync(result);
//        }
//        else
//        {
//            // ?? Production? ???? ????? ???? ???
//            var result = System.Text.Json.JsonSerializer.Serialize(new
//            {
//                message = "??? ??? ??? ?????. ???? ???????? ??????."
//            });
//            await context.Response.WriteAsync(result);
//        }
//    });
//});
////=========================================================================================
//// ============================
////??? ?????? ??????? 
//// ============================
////using (var scope = app.Services.CreateScope())
////{
////    var ctx = scope.ServiceProvider.GetRequiredService<WohnungenContext>();
////    var expired = ctx.Wohnungen.Where(w => w.ExpiresAt < DateTime.UtcNow);
////    ctx.Wohnungen.RemoveRange(expired);
////    ctx.SaveChanges();
////}

//// ============================
//// 8? Middleware ???????? ??????
//// ============================
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}
//else
//{
//    // ?? ???????? ???? Swagger ?????? ????? ?????? ??? API
//    app.UseDeveloperExceptionPage();
//    app.UseSwagger();
//    app.UseSwaggerUI(c => {
//        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Wohnungen API V1");
//        c.RoutePrefix = string.Empty;
//    });
//}

//app.UseStaticFiles();
//app.UseRouting();

//// ?? ??? ????: CORS ??? ?? ???? ??? Routing ???? Authentication
//var corsPolicy = app.Environment.IsDevelopment() ? "DevCors" : "ProdCors";
//app.UseCors(corsPolicy);



//app.UseAuthentication();  // ?? ????
//app.UseAuthorization();  // ???? ??? ???

//app.MapControllers();


//// ============================
//// ????? ????? ???????? ?????? Migrations
//// ============================
//try
//{
//    using (var scope = app.Services.CreateScope())
//    {
//        var services = scope.ServiceProvider;
//        var context = services.GetRequiredService<WohnungenContext>();
//        var logger = services.GetRequiredService<ILogger<Program>>();

//        logger.LogInformation("Checking database connection...");

//        // ? ????? Migrations
//        if (app.Environment.IsDevelopment())
//        {
//            // ?? Development: ???? ??????? EnsureCreated ???????? ??????
//            // ??? Migrate ????
//            await context.Database.MigrateAsync();
//            logger.LogInformation("Database migrated successfully (Development)");
//        }
//        else
//        {
//            // ?? Production: ?????? Migrate ??????
//            await context.Database.MigrateAsync();
//            logger.LogInformation("Database migrated successfully (Production)");
//        }

//        // ? ?????? ?? ???????
//        var canConnect = await context.Database.CanConnectAsync();
//        if (canConnect)
//        {
//            logger.LogInformation("? Database connection successful!");
//        }
//        else
//        {
//            logger.LogError("? Database connection failed!");
//            throw new Exception("Cannot connect to database");
//        }
//    }
//}
//catch (Exception ex)
//{
//    var logger = app.Services.GetRequiredService<ILogger<Program>>();
//    logger.LogError(ex, "An error occurred while migrating the database");

//    // ?? Production? ?? ????? ??????? - ?? Render ????? ????????
//    if (app.Environment.IsDevelopment())
//    {
//        throw; // ?? Development? ???? ?????
//    }
//}
//using (var scope = app.Services.CreateScope())
//{
//    var services = scope.ServiceProvider;
//    var userManager = services.GetRequiredService<UserManager<Benutzer>>();
//    var roleManager = services.GetRequiredService<RoleManager<IdentityRole<int>>>();

//    await IdentitySeeder.SeedAsync(userManager, roleManager);
//}

//app.Run();
