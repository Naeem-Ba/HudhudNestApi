using System.Net;
using System.Net.Http.Headers;
using PropertyApi.Integration.Tests.TestInfrastructure;

namespace PropertyApi.Integration.Tests.Security;

/// <summary>
/// PHASE 5 (Account Deletion) security gate: proves — with a real HTTP round-trip through
/// the actual pipeline, not a unit-level assertion — that DELETE /api/Users/me enforces
/// authentication before anything else, exactly like every other [Authorize] endpoint.
///
/// This deliberately does not attempt the full "delete a real seeded account" path: that
/// needs a real Postgres-backed user (JwtValidationTests' InMemory host has no seeded
/// Identity data and no register/login HTTP flow wired up for it), which is out of reach
/// without the Docker-based Postgres integration environment (see
/// docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md §16 for what remains NOT VERIFIED for that
/// reason). What this test *can* and does prove without any database: an unauthenticated
/// caller — and a caller holding a well-formed but otherwise-invalid token — never reaches
/// DeleteUserCommandHandler at all.
/// </summary>
public sealed class AccountDeletionAuthenticationTests : IClassFixture<TestApplication>
{
    private const string Endpoint = "/api/Users/me";

    private readonly TestApplication _factory;

    public AccountDeletionAuthenticationTests(TestApplication factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "DELETE /api/Users/me with no Authorization header is rejected with 401")]
    public async Task MissingToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "DELETE /api/Users/me with a malformed bearer token is rejected with 401")]
    public async Task MalformedToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "this-is-not-a-jwt");

        var response = await client.DeleteAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
