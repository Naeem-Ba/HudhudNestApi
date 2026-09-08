using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace PropertyApi.Integration.Tests.Marketing;

public sealed class MarketingEventsControllerTests : IClassFixture<TestApplication>
{
    private readonly TestApplication _factory;
    private readonly HttpClient _client;

    public MarketingEventsControllerTests(TestApplication factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Track_ValidPageView_Returns200AndPersists()
    {
        // Compares a before/after delta rather than asserting an absolute count — this
        // class's IClassFixture<TestApplication> shares one database across every test
        // method in it, so an absolute count would depend on method execution order.
        var before = await CountEventsAsync();

        var dto = new
        {
            EventType = "PageView",
            SessionId = "sess-xyz",
            Path = "/landing"
        };

        var response = await _client.PostAsJsonAsync("/api/marketing-events", dto);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode}. Body: {body}");

        Assert.Equal(before + 1, await CountEventsAsync());
    }

    private Task<int> CountEventsAsync() =>
        _factory.InScopeAsync(services =>
            services.GetRequiredService<AppDbContext>().MarketingEvents.CountAsync());

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Track_MissingEventType_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/marketing-events", new { Path = "/landing" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSummary_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/marketing-events/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
