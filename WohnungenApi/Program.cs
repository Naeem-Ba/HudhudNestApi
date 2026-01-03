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

// ============================
// 1️⃣ إعدادات ثقافية (Culture)
// ============================
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// ============================
// 2️⃣ قاعدة البيانات (Database)
// ============================
//builder.Services.AddDbContext<WohnungenContext>(options =>
//{
//    options.UseSqlServer(
//        builder.Configuration.GetConnectionString("DefaultConnection"),
//        sqlOptions =>
//        {
//            sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
//        });

//    options.EnableThreadSafetyChecks(false);
//});
// 2. قاعدة البيانات (تحويل إلى PostgreSQL)
builder.Services.AddDbContext<WohnungenContext>(options =>
{
    // نستخدم UseNpgsql بدلاً من UseSqlServer
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
    npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()); // إعادة المحاولة في حال تأخر السيرفر
});

// ============================
// 3️⃣ إعدادات CORS (هام جدًا)
// ============================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
             "https://realestateworld.world",
            "https://www.realestateworld.world",
            "https://bizorealestateworld.netlify.app"
            ) // الدومين بالضبط
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ============================
// 4️⃣ إعدادات Controllers و JSON
// ============================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// ============================
// 5️⃣ إعدادات JWT
// ============================
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwt = builder.Configuration.GetSection("Jwt");

    options.RequireHttpsMetadata = true; // Render يدعم HTTPS
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

// ============================
// 6️⃣ خدمات أخرى
// ============================
builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ============================
// 7️⃣ بناء التطبيق
// ============================
var app = builder.Build();

// ============================
// 8️⃣ Middleware بالترتيب الصحيح
// ============================

//if (app.Environment.IsDevelopment())
//{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => {
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Wohnungen API V1");
    c.RoutePrefix = "swagger";
    });
//}

app.UseStaticFiles();

app.UseRouting();             // ⚡ يجب أن يسبق CORS
app.UseCors("AllowAngular");  // ⚡ يجب أن يكون بعد Routing وقبل Auth

// مؤقتًا لعرض Exceptions على Production
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = feature?.Error;

        context.Response.ContentType = "application/json";

        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            message = ex?.Message,
            stackTrace = ex?.StackTrace,
            innerException = ex?.InnerException?.Message
        });

        await context.Response.WriteAsync(result);
    });
});

Console.WriteLine($"Globalization Invariant: {CultureInfo.InvariantCulture.Name}");

app.UseAuthentication(); // من أنت؟
app.UseAuthorization();  // ماذا يحق لك؟

app.MapControllers();
// قبل app.Run() أضف هذا لإنشاء الجداول تلقائياً في Render
try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<WohnungenContext>();

        // انتظر قليلاً لضمان استقرار الاتصال في البيئة السحابية
        Console.WriteLine("Checking for pending migrations...");
        context.Database.Migrate();
        Console.WriteLine("Migrations applied successfully!");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred while migrating the database: {ex.Message}");
    // لا تجعل التطبيق ينهار إذا فشل الـ Migration مؤقتاً
}

// تشغيل التطبيق
app.Run();
