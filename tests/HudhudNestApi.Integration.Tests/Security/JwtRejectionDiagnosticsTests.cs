using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// A bearer token that is correctly signed but then rejected by the post-signature checks in
/// <c>JwtAuthenticationRegistration.OnTokenValidated</c> (missing user data, unknown/disabled
/// account, security-stamp mismatch) used to answer a bare 401 with NOTHING in the log: only the
/// exception paths wrote a line, <c>context.Fail(reason)</c> never did. An operator staring at a
/// "login succeeds, next call 401" report had no way to see which check failed. These tests pin
/// that the reason is logged, and that the token itself never is.
/// </summary>
public sealed class JwtRejectionDiagnosticsTests : IClassFixture<TestApplication>
{
    private const string ProtectedEndpoint = "/api/favorites";

    private readonly TestApplication _factory;

    public JwtRejectionDiagnosticsTests(TestApplication factory) => _factory = factory;

    [Fact]
    public async Task ValidlySignedToken_WithoutASecurityStampClaim_Is401_AndTheReasonIsLogged()
    {
        var logs = new CapturingLoggerProvider();
        var token = CreateToken(securityStamp: null);

        var response = await SendAsync(token, logs);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(logs.Warnings, line => line.Contains("does not contain valid user data", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.All, line => line.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidlySignedToken_ForAnUnknownAccount_Is401_AndTheReasonIsLogged()
    {
        var logs = new CapturingLoggerProvider();
        var userId = Guid.NewGuid();
        var token = CreateToken(securityStamp: Guid.NewGuid().ToString("N"), userId: userId);

        var response = await SendAsync(token, logs);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            logs.Warnings,
            line => line.Contains("account is disabled", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains(userId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.All, line => line.Contains(token, StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> SendAsync(string token, CapturingLoggerProvider logs)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.GetAsync(ProtectedEndpoint);
    }

    private static string CreateToken(string? securityStamp, Guid? userId = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecuritySettings.JwtKey)), SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()) };
        if (securityStamp is not null)
            claims.Add(new Claim("security_stamp", securityStamp));

        var jwt = new JwtSecurityToken(
            issuer: TestSecuritySettings.JwtIssuer,
            audience: TestSecuritySettings.JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Text)> _entries = new();

        public IEnumerable<string> All => _entries.Select(entry => entry.Text);

        public IEnumerable<string> Warnings => _entries.Where(entry => entry.Level >= LogLevel.Warning).Select(entry => entry.Text);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Text)> entries, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, $"[{category}] {formatter(state, exception)} {exception}"));
        }
    }
}
