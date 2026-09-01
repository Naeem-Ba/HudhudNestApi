using System.Net;
using System.Net.Http.Json;
using PropertyApi.Integration.Tests.TestInfrastructure;
using PropertyApi.Security.RateLimiting;

namespace PropertyApi.Integration.Tests.Security;

public sealed class RateLimitingTests : IClassFixture<TestApplication>
{
    private readonly TestApplication _factory;

    public RateLimitingTests(TestApplication factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Login endpoint returns 429 after auth-login policy limit is exceeded")]
    public async Task Login_Should_Return429_After_Policy_Limit()
    {
        using var client = _factory.CreateClient();

        // BUG-33: RedisRateLimitingMiddleware.InvokeAsync computes a fixed window bucket
        // as `now.ToUnixTimeMilliseconds() / windowMs` -- i.e. aligned to absolute UTC
        // epoch boundaries, not to this request loop's own start time. If the loop below
        // happens to straddle one of those boundaries (observed in CI: run 33514114705
        // failed this exact assertion while run 33514186722, same commit run in parallel,
        // passed all 87 integration tests), the counter resets mid-loop and the 11th
        // request lands as request #1 of a fresh window (count=1, well under the limit)
        // instead of request #11 of the original window -- so it gets a real 401
        // Unauthorized (wrong password) instead of 429. Waiting here until we're safely
        // inside a fresh window before starting the loop removes the race by construction,
        // using the exact same bucket arithmetic the middleware itself uses, read from the
        // real policy config so a future window-size change can't silently reintroduce this.
        await RateLimitWindowSync.WaitForSafeWindowStartAsync(
            RedisRateLimitingDefaults.Policies["auth-login"].Window);

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

        // BUG-33: same window-boundary hazard as Login_Should_Return429_After_Policy_Limit
        // above, applied defensively here too (auth-register's 10-minute window makes a
        // mid-loop rollover far less likely in practice, but it is the same class of bug).
        await RateLimitWindowSync.WaitForSafeWindowStartAsync(
            RedisRateLimitingDefaults.Policies["auth-register"].Window);

        HttpResponseMessage? lastResponse = null;

        for (var i = 0; i < 120; i++)
        {
            var request = new
            {
                email = $"rate-limit-{Guid.NewGuid():N}@example.com",
                password = "Password123!",
                firstName = "Rate",
                lastName = "Limit",
                preferredLanguage = "ar",
                preferredCurrency = "SYP"
            };

            lastResponse = await client.PostAsJsonAsync("/api/auth/register", request);

            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }
}
