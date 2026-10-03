using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;

namespace HudhudNestApi.Integration.Tests.Favorites;

/// <summary>
/// Core E2E audit F-1: GET /api/favorites returned only ColdRent/PurchasePrice without the property's
/// currency, so the SPA fell back to SYP and showed a USD listing as "1,251 ل.س". The favourite's property
/// must carry the same currency and rent figures the other listing endpoints expose.
/// </summary>
public sealed class FavoritesCurrencyIntegrationTests : IClassFixture<PhoneLoginAuditFactory>, IAsyncLifetime
{
    private const string Password = "SecurePass9";
    private static int _seed = Environment.TickCount & 0xFFFFFF;
    private readonly PhoneLoginAuditFactory _factory;

    public FavoritesCurrencyIntegrationTests(PhoneLoginAuditFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "GET /api/favorites returns the property's currency and warm rent")]
    [Trait("Category", "Integration")]
    public async Task GetFavorites_ReturnsCurrencyAndWarmRent_OfTheFavouritedProperty()
    {
        var phone = $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
        using var client = _factory.CreateClient(new() { HandleCookies = false });
        var send = await client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await send.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        var verify = await client.PostAsJsonAsync("/api/auth/phone/registration/verify",
            new { challengeId, code = _factory.Sms.LastCodeFor(phone), password = Password, firstName = "Fav", lastName = "Currency" });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var access = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        Guid propertyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = await db.Users.Where(u => u.NormalizedPhoneNumber == phone).Select(u => u.Id).SingleAsync();
            var property = Property.Create("USD favourite", "A USD priced listing", userId, ListingType.ForRent, "SY", "USD");
            property.ColdRent = 1250.5m;
            property.WarmRent = 1450.5m;
            db.Properties.Add(property);
            db.Favorites.Add(new Favorite { UserId = userId, PropertyId = property.Id });
            await db.SaveChangesAsync();
            propertyId = property.Id;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/favorites");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var item = Assert.Single(items);
        Assert.Equal(propertyId, item.GetProperty("propertyId").GetGuid());
        var property2 = item.GetProperty("property");
        Assert.Equal("USD", property2.GetProperty("currencyCode").GetString());
        Assert.Equal(1250.5m, property2.GetProperty("coldRent").GetDecimal());
        Assert.Equal(1450.5m, property2.GetProperty("warmRent").GetDecimal());
    }
}
