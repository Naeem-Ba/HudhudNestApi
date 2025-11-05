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
                        .AllowAnyMethod());
});

// Controllers + Swagger
builder.Services.AddControllers();
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
app.UseAuthorization();

app.MapControllers();

app.Run();
