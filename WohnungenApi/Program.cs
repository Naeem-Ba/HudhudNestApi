using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Data;
using WohnungenApi.Models;
using System.Globalization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WohnungenContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ImmobilienDB")));

// CORS aktivieren
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy => policy.WithOrigins("http://localhost:4200")
                        .AllowAnyHeader()
                        .AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddControllers();
var app = builder.Build();
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
//using (var scope = app.Services.CreateScope())
//{
//    var ctx = scope.ServiceProvider.GetRequiredService<WohnungenContext>();
//    if (!ctx.Users.Any())
//    {
//        ctx.Users.Add(new User { DisplayName = "Nimo Bizo", Email = "max@example.com", Phone = "015772378923", IsAgent = false });
//        ctx.SaveChanges();
//    }

//    if (!ctx.Wohnungen.Any())
//    {
//        var owner = ctx.Users.First();
//        var w1 = new Wohnung { Titel = "Moderne Wohnung in Jdjdet Artuz", Beschreibung = "3 Zimmer, Balkon", Stadt = "Damascus", Kaltmiete = 1200M, ZumKaufen = false, Zimmer = 3, Flaeche = 78.5M, ZumMieten = true, OwnerId = owner.Id};
//        ctx.Wohnungen.Add(w1);
//        ctx.SaveChanges();

//        ctx.WohnungImages.Add(new WohnungImage { WohnungId = w1.Id, Url = "/images/w1-1.jpg", IsMain = true});
//        ctx.SaveChanges();
//    }
//}

