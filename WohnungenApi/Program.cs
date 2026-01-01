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

// 1. الإعدادات الثقافية (Culture)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// 2. قاعدة البيانات (Database)
builder.Services.AddDbContext<WohnungenContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. إعدادات الـ CORS (مهمة جداً للموبايل)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)   // يسمح بالاتصال من الموبايل أو أي مكان
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 4. إعدادات الـ Controllers والـ JSON
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // يحافظ على أسماء الحقول كما هي
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 5. إعدادات الـ JWT
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwt = builder.Configuration.GetSection("Jwt");

    options.RequireHttpsMetadata = false; // معطل لأن الاستضافة قد تكون http فقط
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            Console.WriteLine("JWT AUTH FAILED: " + ctx.Exception.Message);
            return Task.CompletedTask;
        }
    };
});

// 6. الخدمات الأخرى
builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- 7. ترتيب الـ Middleware (الترتيب هنا حاسم جداً) ---

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// الترتيب الصحيح للـ Middleware لتجنب مشاكل CORS و Auth
app.UseStaticFiles();
// مؤقتًا لعرض Exceptions على Production
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = feature?.Error;

        context.Response.ContentType = "application/json";

        // Json output كامل للخطأ
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            message = ex?.Message,
            stackTrace = ex?.StackTrace,
            innerException = ex?.InnerException?.Message
        });

        await context.Response.WriteAsync(result);
    });
});
Console.WriteLine($"Globalization Invariant: {System.Globalization.CultureInfo.InvariantCulture.Name}");

app.UseRouting(); // يجب أن يسبق CORS

app.UseCors("AllowAngular"); // يجب أن يكون بعد Routing وقبل Auth

app.UseAuthentication(); // من أنت؟
app.UseAuthorization();  // ماذا يحق لك؟

app.MapControllers();

app.Run();