using System.Net;
using System.Net.Http.Headers;
using HudhudNestApi.Configuration;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Covers two session-audit findings (2026-10) for GET /api/properties and
/// GET /api/properties/{id}: (1) [OutputCache] caches server-side but never sends
/// Cache-Control to the browser/CDN on its own -- PropertiesController now sets it explicitly;
/// (2) no response compression was configured anywhere, so even a correctly cached response
/// still shipped uncompressed JSON over the wire. Both exercised through the real HTTP pipeline
/// (TestApplication/WebApplicationFactory), not by calling the action methods directly, since
/// headers set by middleware (compression) and the ones the action sets itself both only really
/// prove anything when observed on an actual HTTP response.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertiesCacheAndCompressionTests : IClassFixture<TestApplication>
{
    private readonly TestApplication _factory;

    public PropertiesCacheAndCompressionTests(TestApplication factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "GET /api/properties sends a public Cache-Control matching the list policy's TTL")]
    public async Task GetAll_Sets_CacheControl_Matching_List_Policy_MaxAge()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/properties");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(OutputCacheRegistration.PublicPropertyListMaxAge, response.Headers.CacheControl.MaxAge);
    }

    [Fact(DisplayName = "GET /api/properties/{id} sends a public Cache-Control matching the details policy's TTL, even on a 404")]
    public async Task GetById_Sets_CacheControl_Matching_Details_Policy_MaxAge_On_NotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/properties/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(OutputCacheRegistration.PublicPropertyDetailsMaxAge, response.Headers.CacheControl.MaxAge);
    }

    [Fact(DisplayName = "GET /api/properties compresses its JSON when the client advertises gzip support")]
    public async Task GetAll_Compresses_Response_When_Client_Accepts_Gzip()
    {
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/properties");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "A second identical request, served from the OutputCache hit, still carries the same Cache-Control")]
    public async Task GetAll_Second_Request_Served_From_Cache_Still_Has_CacheControl()
    {
        // Round-3 audit verification: Cache-Control is set by the controller action itself, not
        // by [OutputCache] -- worth confirming it survives on a CACHE HIT too (the action method
        // body does not even run the second time), not just on the first request that populates
        // the cache. OutputCache replays the whole captured response (status, headers, body) on
        // a hit, so this should hold, but it is exactly the kind of subtlety worth a real
        // assertion rather than an assumption.
        using var client = _factory.CreateClient();

        var first = await client.GetAsync("/api/properties?page=1&pageSize=5");
        var second = await client.GetAsync("/api/properties?page=1&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(second.Headers.CacheControl);
        Assert.True(second.Headers.CacheControl!.Public);
        Assert.Equal(OutputCacheRegistration.PublicPropertyListMaxAge, second.Headers.CacheControl.MaxAge);
    }
}
