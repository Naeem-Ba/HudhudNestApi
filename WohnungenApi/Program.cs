using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WohnungenApi.Data;

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
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthorization();
app.UseDeveloperExceptionPage();
app.MapControllers();

app.Run();
