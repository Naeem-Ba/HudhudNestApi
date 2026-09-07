using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
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
/// Phase 6 (Authentication production readiness) — closes a real test-coverage gap: before
/// this class, AppleTokenVerifier's cryptographic validation (issuer/audience/expiry/signature)
/// was only proven by reading its `TokenValidationParameters` configuration, never by actually
/// throwing a forged/expired/mis-scoped token at it, unlike the app's own JWT
/// (Security/JwtValidationTests.cs). Same rationale as that class: "the config looks right" and
/// "a crafted malicious token is actually rejected" are different claims, and only the second
/// one is real evidence.
///
/// Reuses AppleTokenVerifierNonceTests' harness pattern (mocked JWKS endpoint, real RSA-signed
/// tokens — never a hand-typed string, since a malformed token would trivially fail for the
/// wrong reason).
/// </summary>
public sealed class AppleTokenVerifierValidationTests
{
    private const string ClientId = "com.example.app";
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string Subject = "apple-user-sub-123";

    [Fact]
    public async Task VerifyAsync_Fails_WhenTheAudienceDoesNotMatchOurClientId()
    {
        var context = CreateVerifier();
        var token = context.IssueToken(audience: "some-other-app.example.com");

        var result = await context.Verifier.VerifyAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_Fails_WhenTheIssuerIsNotAppleId()
    {
        var context = CreateVerifier();
        var token = context.IssueToken(issuer: "https://not-apple.example.com");

        var result = await context.Verifier.VerifyAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_Fails_WhenTheTokenIsExpired()
    {
        var context = CreateVerifier();
        var token = context.IssueToken(
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-10));

        var result = await context.Verifier.VerifyAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_Fails_WhenSignedByAKeyApplePublishedDidNotIssue()
    {
        // Simulates an attacker who controls no Apple private key: the JWKS endpoint returns
        // the real (test) Apple key, but the token is signed with an entirely different key
        // pair — the classic "forged signature" attack this audit is required to test for.
        var context = CreateVerifier();
        using var attackerRsa = RSA.Create(2048);
        var token = context.IssueToken(signingRsaOverride: attackerRsa, keyIdOverride: context.KeyId);

        var result = await context.Verifier.VerifyAsync(token);

        Assert.Null(result);
    }

    // Note: a "kid the JWKS doesn't advertise" case was deliberately not added here.
    // Microsoft.IdentityModel.Tokens treats "kid" as a routing hint, not a hard requirement —
    // when a token's declared kid matches none of IssuerSigningKeys, ValidateToken still tries
    // every supplied key rather than failing outright. That is standard, spec-compliant JWT
    // library behavior (kid exists to make lookup efficient during key rotation, not to gate
    // trust) and is *not* a vulnerability: the security property that actually matters —
    // rejecting a signature made with a key Apple never published — is what
    // VerifyAsync_Fails_WhenSignedByAKeyApplePublishedDidNotIssue below proves.

    [Fact]
    public async Task VerifyAsync_Fails_WhenThePayloadIsTamperedAfterSigning()
    {
        // Splices a different "sub" into an otherwise-validly-signed token's payload segment —
        // mirrors Security/JwtValidationTests.cs's "tampered payload" case for the app's own JWT.
        var context = CreateVerifier();
        var token = context.IssueToken();
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var tamperedPayload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    """{"sub":"attacker-controlled-subject","iss":"https://appleid.apple.com","aud":"com.example.app","exp":9999999999}"""))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var result = await context.Verifier.VerifyAsync(tampered);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_Succeeds_ForAGenuinelyValidToken()
    {
        // Positive control: proves the negative tests above are failing for the right reason
        // (a real defect in the token), not because the test harness itself is broken.
        var context = CreateVerifier();
        var token = context.IssueToken();

        var result = await context.Verifier.VerifyAsync(token);

        Assert.NotNull(result);
        Assert.Equal(Subject, result!.ProviderId);
        Assert.Equal("Apple", result.ProviderName);
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

        public VerifierContext(AppleTokenVerifier verifier, RSA signingRsa, string keyId)
        {
            Verifier = verifier;
            _signingRsa = signingRsa;
            KeyId = keyId;
        }

        public AppleTokenVerifier Verifier { get; }

        public string KeyId { get; }

        public string IssueToken(
            string? issuer = null,
            string? audience = null,
            DateTime? notBefore = null,
            DateTime? expires = null,
            RSA? signingRsaOverride = null,
            string? keyIdOverride = null)
        {
            var claims = new List<Claim>
            {
                new("sub", Subject),
                new("email", "user@example.com"),
                new("email_verified", "true")
            };

            var signingCredentials = new SigningCredentials(
                new RsaSecurityKey(signingRsaOverride ?? _signingRsa) { KeyId = keyIdOverride ?? KeyId },
                SecurityAlgorithms.RsaSha256);

            var jwt = new JwtSecurityToken(
                issuer: issuer ?? AppleIssuer,
                audience: audience ?? ClientId,
                claims: claims,
                notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
                expires: expires ?? DateTime.UtcNow.AddMinutes(10),
                signingCredentials: signingCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
