using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using HudhudNestApi.Configuration;
using Moq;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Rebrand transition: tokens issued under the pre-rename issuer/audience must keep validating
/// (Jwt:AdditionalValid*), tokens under the new values must validate, anything else must not.
/// </summary>
public sealed class JwtIssuerTransitionTests
{
    private static readonly string Key = TestInfrastructure.TestSecuritySettings.JwtKey;

    private static TokenValidationParameters BuildParameters(bool withLegacy)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = "HudhudNest",
            ["Jwt:Audience"] = "HudhudNestClient",
        };
        if (withLegacy)
        {
            values["Jwt:AdditionalValidIssuers:0"] = "PropertyApi";
            values["Jwt:AdditionalValidAudiences:0"] = "PropertyApiClient";
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHudhudNestApiJwtAuthentication(config, env.Object);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
    }

    private static string Token(string issuer, string audience)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(issuer, audience, new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30), creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static bool IsValid(TokenValidationParameters p, string token)
    {
        try { new JwtSecurityTokenHandler().ValidateToken(token, p, out _); return true; }
        catch (SecurityTokenException) { return false; }
    }

    [Fact]
    public void NewIssuerAndAudience_AreAccepted() =>
        Assert.True(IsValid(BuildParameters(true), Token("HudhudNest", "HudhudNestClient")));

    [Fact]
    public void LegacyIssuerAndAudience_AreAcceptedDuringTransition() =>
        Assert.True(IsValid(BuildParameters(true), Token("PropertyApi", "PropertyApiClient")));

    [Fact]
    public void LegacyValues_AreRejectedOnceTransitionEntriesAreRemoved() =>
        Assert.False(IsValid(BuildParameters(false), Token("PropertyApi", "PropertyApiClient")));

    [Fact]
    public void UnknownIssuer_IsRejected() =>
        Assert.False(IsValid(BuildParameters(true), Token("Evil", "HudhudNestClient")));

    [Fact]
    public void UnknownAudience_IsRejected() =>
        Assert.False(IsValid(BuildParameters(true), Token("HudhudNest", "Evil")));
}
