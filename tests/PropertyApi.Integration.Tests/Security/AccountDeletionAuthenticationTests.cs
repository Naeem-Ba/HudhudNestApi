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

    // Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): the two endpoints added
    // alongside the delay window get the same no-database-needed auth proof as DELETE
    // /api/Users/me above. The real Postgres-backed behavior of both (a due deletion actually
    // executing, cancellation actually clearing the schedule, export actually returning only
    // the caller's own data) is covered separately by AccountDeletionSweepTests and
    // AccountDataExportTests, now that a real Postgres integration environment is available.

    [Fact(DisplayName = "POST /api/Users/me/deletion/cancel with no Authorization header is rejected with 401")]
    public async Task CancelDeletion_MissingToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/Users/me/deletion/cancel", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "POST /api/Users/me/deletion/cancel with a malformed bearer token is rejected with 401")]
    public async Task CancelDeletion_MalformedToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "this-is-not-a-jwt");

        var response = await client.PostAsync("/api/Users/me/deletion/cancel", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "GET /api/Users/me/export with no Authorization header is rejected with 401")]
    public async Task Export_MissingToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Users/me/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "GET /api/Users/me/export with a malformed bearer token is rejected with 401")]
    public async Task Export_MalformedToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "this-is-not-a-jwt");

        var response = await client.GetAsync("/api/Users/me/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
