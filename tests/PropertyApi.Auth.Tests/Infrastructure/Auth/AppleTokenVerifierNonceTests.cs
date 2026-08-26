using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using PropertyApi.Infrastructure.Auth;
using PropertyApi.Infrastructure.Settings;
using Xunit;

namespace PropertyApi.Auth.Tests.Infrastructure.Auth;

/// <summary>
/// AppleTokenVerifier's nonce enforcement (B-16, RELEASE-BLOCKERS-AR.md): a valid Apple
/// identity token proves Apple issued it, not that it was issued for this sign-in attempt.
/// The JWKS fetch is stubbed the same way ResendEmailSenderTests stubs its outbound HTTP
/// call, since real verification needs a real RSA-signed token, not a hand-typed string.
/// </summary>
public sealed class AppleTokenVerifierNonceTests
{
    private const string ClientId = "com.example.app";
    private const string Subject = "apple-user-sub-123";

    [Fact]
    public async Task VerifyAsync_Succeeds_WhenTheNonceMatches()
    {
        var context = CreateVerifier();
        const string rawNonce = "client-generated-raw-nonce";
        var token = context.IssueToken(nonceClaim: HashNonce(rawNonce));

        var result = await context.Verifier.VerifyAsync(token, expectedNonce: rawNonce);

        Assert.NotNull(result);
        Assert.Equal(Subject, result!.ProviderId);
    }

    [Fact]
    public async Task VerifyAsync_Fails_WhenTheNonceDoesNotMatch()
    {
        var context = CreateVerifier();
        var token = context.IssueToken(nonceClaim: HashNonce("the-nonce-apple-actually-saw"));

        var result = await context.Verifier.VerifyAsync(token, expectedNonce: "a-different-raw-nonce");

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_Fails_WhenTheTokenHasNoNonceClaim_ButOneWasExpected()
    {
        var context = CreateVerifier();
        var token = context.IssueToken(nonceClaim: null);

        var result = await context.Verifier.VerifyAsync(token, expectedNonce: "some-raw-nonce");

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_Succeeds_WhenNoNonceWasExpected_RegardlessOfTheTokenClaim()
    {
        // Backward compatible by design: a caller that does not pass expectedNonce (nothing
        // in this codebase should reach Production without one, but the parameter is
        // optional at the interface level) gets the pre-B-16 behaviour.
        var context = CreateVerifier();
        var token = context.IssueToken(nonceClaim: null);

        var result = await context.Verifier.VerifyAsync(token);

        Assert.NotNull(result);
    }

    private static string HashNonce(string rawNonce)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static VerifierContext CreateVerifier()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        const string keyId = "test-key-1";

        var jwks = $$"""
            {"keys":[{"kty":"RSA","kid":"{{keyId}}","use":"sig","alg":"RS256",
            "n":"{{Base64UrlEncode(parameters.Modulus!)}}",
            "e":"{{Base64UrlEncode(parameters.Exponent!)}}"}]}
            """;

        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jwks)
            });

        var httpClient = new HttpClient(handler.Object);
        var settings = Options.Create(new SocialAuthSettings { AppleClientId = ClientId });
        var verifier = new AppleTokenVerifier(
            httpClient,
            new MemoryCache(new MemoryCacheOptions()),
            settings,
            NullLogger<AppleTokenVerifier>.Instance);

        // Exported once and reused by the RSA private key still held by the closure below —
        // ExportParameters(true) on the same RSA instance would work too, but keeping the
        // signing key alive via a captured RSA instance is clearer than re-importing it.
        var signingRsa = RSA.Create();
        signingRsa.ImportParameters(rsa.ExportParameters(true));

        return new VerifierContext(verifier, signingRsa, keyId);
    }

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private sealed class VerifierContext
    {
        private readonly RSA _signingRsa;
        private readonly string _keyId;

        public VerifierContext(AppleTokenVerifier verifier, RSA signingRsa, string keyId)
        {
            Verifier = verifier;
            _signingRsa = signingRsa;
            _keyId = keyId;
        }

        public AppleTokenVerifier Verifier { get; }

        public string IssueToken(string? nonceClaim)
        {
            var claims = new List<Claim>
            {
                new("sub", Subject),
                new("email", "user@example.com"),
                new("email_verified", "true")
            };

            if (nonceClaim is not null)
            {
                claims.Add(new Claim("nonce", nonceClaim));
            }

            var signingCredentials = new SigningCredentials(
                new RsaSecurityKey(_signingRsa) { KeyId = _keyId },
                SecurityAlgorithms.RsaSha256);

            var jwt = new JwtSecurityToken(
                issuer: "https://appleid.apple.com",
                audience: ClientId,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: signingCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
