using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using System.IdentityModel.Tokens.Jwt;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// PHASE 3 SECURITY GATE (§4, §60): proves — with a real HTTP round-trip through the
/// actual JwtBearer middleware, not a unit-level assertion on
/// <see cref="TokenValidationParameters"/> — that a protected endpoint rejects every
/// class of invalid bearer token with 401, and never with a 500 or a 200.
///
/// Uses <see cref="TestApplication"/>'s "Testing" host (InMemory EF, real Jwt:Key /
/// Jwt:Issuer / Jwt:Audience from <see cref="TestSecuritySettings"/>) so no external
/// database or Redis is required. A GET against a plain [Authorize]-only endpoint
/// (favorites) is enough — the JwtBearer handler runs (and rejects) before the
/// controller action, and before the security-stamp lookup in
/// JwtAuthenticationRegistration.OnTokenValidated even fires for these malformed
/// tokens, so no seeded user is required either.
/// </summary>
public sealed class JwtValidationTests : IClassFixture<TestApplication>
{
    private const string ProtectedEndpoint = "/api/favorites";

    private readonly TestApplication _factory;

    public JwtValidationTests(TestApplication factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "No Authorization header is rejected with 401")]
    public async Task MissingToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ProtectedEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Expired access token is rejected with 401")]
    public async Task ExpiredToken_Returns401()
    {
        var token = CreateToken(expiresInMinutes: -5);

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Malformed token (not a JWT at all) is rejected with 401")]
    public async Task MalformedToken_Returns401()
    {
        var response = await SendWithTokenAsync("this-is-not-a-jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Token signed with the wrong issuer is rejected with 401")]
    public async Task WrongIssuer_Returns401()
    {
        var token = CreateToken(issuer: "SomeOtherIssuer");

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Token signed with the wrong audience is rejected with 401")]
    public async Task WrongAudience_Returns401()
    {
        var token = CreateToken(audience: "SomeOtherClient");

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Token signed with a different (attacker-controlled) key is rejected with 401")]
    public async Task InvalidSignature_Returns401()
    {
        // Correct header/issuer/audience/claims/expiry — only the signing key differs.
        // This is the "attacker forges their own token" scenario: it proves the server
        // actually verifies the signature against its own configured Jwt:Key rather
        // than merely checking the token's shape.
        var token = CreateToken(signingKey: "attacker-controlled-key-0123456789012345");

        var response = await SendWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Token with a tampered payload (role escalation attempt) is rejected with 401")]
    public async Task TamperedPayload_Returns401()
    {
        // Mint a validly-signed token, then splice in a different (attacker-desired)
        // middle segment without re-signing — simulates an attacker editing the base64
        // payload of an intercepted/guessed token to add a role or change the subject.
        var token = CreateToken();
        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);

        var tamperedPayload = CreateToken(subject: Guid.NewGuid().ToString())
            .Split('.')[1];

        var tamperedToken = $"{segments[0]}.{tamperedPayload}.{segments[2]}";

        var response = await SendWithTokenAsync(tamperedToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Token signed with the 'none' algorithm is rejected with 401")]
    public async Task NoneAlgorithm_Returns401()
    {
        // Classic JWT library bypass attempt: an attacker sets alg=none and strips the
        // signature, hoping a permissive verifier treats the token as pre-validated.
        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(
            $$"""
            {"sub":"{{Guid.NewGuid()}}","iss":"{{TestSecuritySettings.JwtIssuer}}","aud":"{{TestSecuritySettings.JwtAudience}}","exp":{{DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()}}}
            """);

        var response = await SendWithTokenAsync($"{header}.{payload}.");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(string token)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return await client.GetAsync(ProtectedEndpoint);
    }

    private static string CreateToken(
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        int expiresInMinutes = 30,
        string? subject = null)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(signingKey ?? TestSecuritySettings.JwtKey));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject ?? Guid.NewGuid().ToString()),
            new Claim("securityStamp", Guid.NewGuid().ToString("N"))
        };

        var jwt = new JwtSecurityToken(
            issuer: issuer ?? TestSecuritySettings.JwtIssuer,
            audience: audience ?? TestSecuritySettings.JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(expiresInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string Base64UrlEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
