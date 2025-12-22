using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WohnungenApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using WohnungenApi.Models;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Culture
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;


// DbContext
builder.Services.AddDbContext<WohnungenContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy => policy.WithOrigins("https://localhost:4200")
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials());
});

// Controllers + Swagger
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // اجعل الـ JSON يستخدم نفس أسماء الخصائص كما هي في الـ C# (بدون lowercase)
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// JWT Options
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt")
);

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwt = builder.Configuration.GetSection("Jwt");

    options.RequireHttpsMetadata = true;   // Production-ready
    options.SaveToken = false;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["Key"]!)
        ),

        ClockSkew = TimeSpan.FromSeconds(30)
    };

    // 🔍 تشخيص واضح (اختياري لكنه مهم)
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            Console.WriteLine("JWT AUTH FAILED: " + ctx.Exception.Message);
            return Task.CompletedTask;
        },
        OnTokenValidated = ctx =>
        {
            Console.WriteLine("JWT OK: " +
                ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");  // أولاً الـ CORS
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();  // ثانياً الـ Authentication (من أنت؟)
app.UseAuthorization();  // ثالثاً الـ Authorization (ماذا يحق لك؟)

app.MapControllers();  // أخيراً المابينج

app.Run();
