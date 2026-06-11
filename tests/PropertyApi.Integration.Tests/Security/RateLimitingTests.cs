using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PropertyApi.Integration.Tests.Security;

public sealed class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });
    }

    [Fact(DisplayName = "Login endpoint returns 429 after auth-login policy limit is exceeded")]
    public async Task Login_Should_Return429_After_Policy_Limit()
    {
        using var client = _factory.CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 11; i++)
        {
            lastResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = $"missing-{i}@example.com",
                Password = "WrongPassword123"
            });
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }

    [Fact(DisplayName = "Register endpoint returns 429 after auth-register policy limit is exceeded")]
    public async Task Register_Should_Return429_After_Policy_Limit()
    {
        using var client = _factory.CreateClient();

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 6; i++)
        {
            lastResponse = await client.PostAsJsonAsync("/api/auth/register", new
            {
                FirstName = "Rate",
                LastName = "Limit",
                Email = $"rate-limit-{Guid.NewGuid():N}@example.com",
                Password = "Password123"
            });
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }
}
