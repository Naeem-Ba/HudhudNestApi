using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P8: map points/clusters and the owner's "hide exact spot" choice, in an empty sea box.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class PropertyMapTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"pm{Guid.NewGuid():N}"[..12];
    // A box in the South Atlantic picked per run: no other test puts listings there.
    private readonly decimal _lat = -40m - Random.Shared.Next(0, 9);
    private readonly decimal _lng = -20m - Random.Shared.Next(0, 9);
    private Guid _owner;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _owner = (await _factory.SeedUserAsync($"{_marker}@test.local")).UserAccountId;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private string Box(int zoom) =>
        $"/api/properties/map?minLat={_lat}&minLng={_lng}&maxLat={_lat + 0.5m}&maxLng={_lng + 0.5m}&zoom={zoom}";

    [Fact(DisplayName = "Few listings come as points; a hidden spot is approximate everywhere the public looks; the owner sees it exact")]
    public async Task Points_And_Privacy()
    {
        var exact = await SeedAsync(_lat + 0.10123m, _lng + 0.20123m, hide: false);
        var hidden = await SeedAsync(_lat + 0.30123m, _lng + 0.30987m, hide: true);
        var draft = await SeedAsync(_lat + 0.2m, _lng + 0.2m, hide: false, published: false);

        var anonymous = _factory.CreateClient();
        var map = await JsonAsync(anonymous, Box(12));
        Assert.Equal("points", map.GetProperty("mode").GetString());
        Assert.Equal(2, map.GetProperty("total").GetInt32());
        var points = map.GetProperty("points").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());
        Assert.False(points.ContainsKey(draft));
        Assert.Equal(_lat + 0.10123m, points[exact].GetProperty("latitude").GetDecimal());
        Assert.False(points[exact].GetProperty("isLocationApproximate").GetBoolean());
        Assert.True(points[hidden].GetProperty("isLocationApproximate").GetBoolean());
        Assert.NotEqual(_lat + 0.30123m, points[hidden].GetProperty("latitude").GetDecimal());
        Assert.True(Math.Abs(points[hidden].GetProperty("latitude").GetDecimal() - (_lat + 0.30123m)) < 0.005m);
        Assert.False(points[hidden].TryGetProperty("ownerId", out _));

        var detail = await JsonAsync(anonymous, $"/api/properties/{hidden}");
        Assert.True(detail.GetProperty("isLocationApproximate").GetBoolean());
        Assert.Equal(string.Empty, detail.GetProperty("street").GetString());
        Assert.Equal(points[hidden].GetProperty("latitude").GetDecimal(), detail.GetProperty("latitude").GetDecimal());

        var near = await JsonAsync(anonymous, $"/api/properties/geo-search?latitude={_lat + 0.3m}&longitude={_lng + 0.3m}&radiusKm=5");
        var nearHidden = Assert.Single(near.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == hidden);
        Assert.True(nearHidden.GetProperty("isLocationApproximate").GetBoolean());
        Assert.Equal(0, nearHidden.GetProperty("distanceMeters").GetDouble() % 500d);

        var manage = await JsonAsync(await ClientAsync(_owner), $"/api/properties/{hidden}/manage");
        Assert.Equal(_lat + 0.30123m, manage.GetProperty("latitude").GetDecimal());
        Assert.True(manage.GetProperty("hideExactLocation").GetBoolean());
        Assert.Equal("Main street 1", manage.GetProperty("street").GetString());
    }

    [Fact(DisplayName = "Many listings come as clusters whose counts add up; a bad box is rejected")]
    public async Task Clusters()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 310; i++)
                db.Properties.Add(New(_lat + 0.01m + (i % 20) * 0.02m, _lng + 0.01m + (i / 20) * 0.02m, hide: i % 2 == 0));
            await db.SaveChangesAsync();
        }

        var map = await JsonAsync(_factory.CreateClient(), Box(9));
        Assert.Equal("clusters", map.GetProperty("mode").GetString());
        Assert.Equal(310, map.GetProperty("total").GetInt32());
        Assert.Empty(map.GetProperty("points").EnumerateArray());
        Assert.Equal(310, map.GetProperty("clusters").EnumerateArray().Sum(c => c.GetProperty("count").GetInt32()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _factory.CreateClient().GetAsync($"/api/properties/map?minLat=10&minLng=10&maxLat=5&maxLng=12&zoom=8")).StatusCode);
    }

    private Property New(decimal lat, decimal lng, bool hide, bool published = true)
    {
        var p = Property.Create($"{_marker} map", "وصف كافٍ للإعلان", _owner, ListingType.ForSale, currencyCode: "USD");
        p.PurchasePrice = 50_000m;
        p.Street = "Main street 1";
        p.Latitude = lat;
        p.Longitude = lng;
        p.HideExactLocation = hide;
        if (!published)
            p.Unpublish();
        return p;
    }

    private async Task<Guid> SeedAsync(decimal lat, decimal lng, bool hide, bool published = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = New(lat, lng, hide, published);
        db.Properties.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<HttpClient> ClientAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("user");
        var roles = await users.GetRolesAsync(user);
        var token = tokens.GenerateAccessToken(new AccessTokenSubject(user.Id, user.Email, user.UserName, user.SecurityStamp), roles.ToArray());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
