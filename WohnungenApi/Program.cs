using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WohnungenApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using WohnungenApi.Models;
using System.Security.Claims;
using WohnungenApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================
// 1 إعدادات ثقافية (Culture)
// ============================
// 1. إعدادات الثقافة لضمان توافق الأرقام العشرية (مثل Latitude/Longitude)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// ============================
// 2 قاعدة البيانات (Database)
// ============================
// 2. قاعدة بيانات PostgreSQL مع دعم التحويل من رابط Render
builder.Services.AddDbContext<WohnungenContext>(options =>
{
    var env = builder.Environment.EnvironmentName;
    var connUrl = builder.Configuration.GetConnectionString("DefaultConnection");
    // تنظيف النص
    connUrl = connUrl?.Trim();

    if (string.IsNullOrWhiteSpace(connUrl))
        throw new Exception("Connection string not found!");

    // Production (Render)
    if (!string.IsNullOrEmpty(connUrl) && (connUrl.StartsWith("postgres://") || connUrl.StartsWith("postgresql://")))
    {
        var uri = new Uri(connUrl);
        var db = uri.AbsolutePath.TrimStart('/');
        var user = uri.UserInfo.Split(':')[0];
        var passwd = uri.UserInfo.Split(':')[1];
        var port = uri.Port > 0 ? uri.Port : 5432;
        connUrl = $"Host={uri.Host};Port={port};Database={db};Username={user};Password={passwd};SSL Mode=Require;Trust Server Certificate=true;";
    }
    options.UseNpgsql(connUrl);
    Console.WriteLine($"ENV = {builder.Environment.EnvironmentName}");
    Console.WriteLine($"DB = {connUrl}");
});


// ============================
// 3 إعدادات CORS (هام جدًا)
// ============================
// 3. إضافة الخدمات والـ Cors
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAngular", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});




// ============================
// 4️ إعدادات Controllers و JSON
// ============================
// 4. إعدادات Cloudinary

builder.Services.AddControllers().AddJsonOptions(options => {
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

//تعطيل PhotoService محليًا
if (!string.IsNullOrEmpty(builder.Configuration["CLOUDINARY_URL"]))
{
    builder.Services.AddScoped<IPhotoService, PhotoService>();// PhotoService فقط هذا السطر لتفعيل عالنت
}
// ============================
// 6️ خدمات أخرى
// ============================
builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ============================
// 7️ بناء التطبيق
// ============================
var app = builder.Build();

//============================= مؤقتًا لعرض Exceptions على Production
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = feature?.Error;

        context.Response.ContentType = "application/json";
        // إضافة Headers الـ CORS يدوياً هنا لضمان وصول رسالة الخطأ للمتصفح
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            message = ex?.Message,
            stackTrace = ex?.StackTrace,
            innerException = ex?.InnerException?.Message
        });

        await context.Response.WriteAsync(result);
    });
});
//=========================================================================================
// ============================
//حذف تلقائي للاعلان 
// ============================
//using (var scope = app.Services.CreateScope())
//{
//    var ctx = scope.ServiceProvider.GetRequiredService<WohnungenContext>();
//    var expired = ctx.Wohnungen.Where(w => w.ExpiresAt < DateTime.UtcNow);
//    ctx.Wohnungen.RemoveRange(expired);
//    ctx.SaveChanges();
//}

// ============================
// 8️ Middleware بالترتيب الصحيح
// ============================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // في الإنتاج، اجعل Swagger متاحاً أيضاً لتجربة الـ API
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Wohnungen API V1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseStaticFiles();
app.UseRouting();

// 🛑 هام جداً: CORS يجب أن يكون بعد Routing وقبل Authentication
app.UseCors("AllowAngular");



app.UseAuthentication();  // من أنت؟
app.UseAuthorization();  // ماذا يحق لك؟

app.MapControllers();


// قبل app.Run()  لإنشاء الجداول تلقائياً في Render
try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<WohnungenContext>();

        // سطر إضافي مؤقت لحذف القاعدة القديمة
        // انتبه: سيؤدي هذا لحذف كل البيانات المسجلة حالياً!
        //context.Database.EnsureDeleted();
        // يقوم بإنشاء الجداول إذا لم تكن موجودة
        context.Database.EnsureCreated();
        Console.WriteLine("Database and Tables created successfully!");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred while migrating the database: {ex.Message}");
    // لا تجعل التطبيق ينهار إذا فشل الـ Migration مؤقتاً
}


app.Run();