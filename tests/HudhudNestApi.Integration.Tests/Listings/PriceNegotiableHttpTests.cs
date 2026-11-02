using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.Messaging;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// "Price is negotiable" over real HTTP and PostgreSQL: the owner sets it on edit, the detail and management
/// views return it, the public list does not, and an edit that omits it keeps the stored value.
/// </summary>
[Trait("Feature", "Listings")]
public sealed class PriceNegotiableHttpTests : IClassFixture<MessagingApiTestFactory>, IAsyncLifetime
{
    private readonly MessagingApiTestFactory _factory;

    public PriceNegotiableHttpTests(MessagingApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact(DisplayName = "Negotiable price: saved on edit, shown on detail and manage, absent from the public list, kept by a later edit")]
    public async Task Flag_RoundTrips_AndStaysOutOfLists()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id, "شقة قابلة للتفاوض");
        var anonymous = _factory.CreateClient();
        var ownerClient = _factory.AuthedClient(owner.AccessToken);

        // Default: not negotiable.
        var before = await Json(await anonymous.GetAsync($"/api/Properties/{propertyId}"));
        Assert.False(before.GetProperty("isPriceNegotiable").GetBoolean());

        var put = await ownerClient.PutAsJsonAsync($"/api/Properties/{propertyId}", new { isPriceNegotiable = true });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        Assert.True((await Json(await anonymous.GetAsync($"/api/Properties/{propertyId}"))).GetProperty("isPriceNegotiable").GetBoolean());
        Assert.True((await Json(await ownerClient.GetAsync($"/api/Properties/{propertyId}/manage"))).GetProperty("isPriceNegotiable").GetBoolean());

        // The public list (cards) must not carry it.
        var list = await Json(await anonymous.GetAsync("/api/Properties"));
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == propertyId);
        Assert.True(!item.TryGetProperty("isPriceNegotiable", out var flag) || flag.ValueKind == JsonValueKind.Null);

        // An edit that omits the flag keeps it; an explicit false turns it off.
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PutAsJsonAsync($"/api/Properties/{propertyId}", new { title = "شقة قابلة للتفاوض جداً" })).StatusCode);
        Assert.True((await Json(await anonymous.GetAsync($"/api/Properties/{propertyId}"))).GetProperty("isPriceNegotiable").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PutAsJsonAsync($"/api/Properties/{propertyId}", new { isPriceNegotiable = false })).StatusCode);
        Assert.False((await Json(await anonymous.GetAsync($"/api/Properties/{propertyId}"))).GetProperty("isPriceNegotiable").GetBoolean());
    }
}
