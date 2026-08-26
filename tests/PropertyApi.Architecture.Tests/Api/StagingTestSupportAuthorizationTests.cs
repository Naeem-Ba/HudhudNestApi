using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PropertyApi.Security.Staging;

namespace PropertyApi.Architecture.Tests.Api;

/// <summary>
/// Covers the shared secret gate behind RELEASE-BLOCKERS-AR.md B-8 (build-info) and
/// StagingTestSupportController's cleanup endpoint. Both are anonymous-by-attribute but
/// meant to answer only Staging automation that knows the configured secret — these tests
/// pin the four ways that must fail closed.
/// </summary>
public sealed class StagingTestSupportAuthorizationTests
{
    private const string Secret = "test-only-staging-secret";

    [Fact(DisplayName = "Matching secret in Staging with test support enabled is authorized")]
    public void MatchingSecret_InStaging_IsAuthorized()
    {
        var request = BuildRequest(Secret);
        var configuration = BuildConfiguration(secret: Secret, enabled: true);

        Assert.True(StagingTestSupportAuthorization.IsAuthorized(
            request, configuration, new TestHostEnvironment("Staging")));
    }

    [Fact(DisplayName = "Wrong secret is rejected")]
    public void WrongSecret_IsRejected()
    {
        var request = BuildRequest("not-the-secret");
        var configuration = BuildConfiguration(secret: Secret, enabled: true);

        Assert.False(StagingTestSupportAuthorization.IsAuthorized(
            request, configuration, new TestHostEnvironment("Staging")));
    }

    [Fact(DisplayName = "Missing header is rejected")]
    public void MissingHeader_IsRejected()
    {
        var request = new DefaultHttpContext().Request;
        var configuration = BuildConfiguration(secret: Secret, enabled: true);

        Assert.False(StagingTestSupportAuthorization.IsAuthorized(
            request, configuration, new TestHostEnvironment("Staging")));
    }

    [Fact(DisplayName = "Correct secret outside Staging is still rejected")]
    public void CorrectSecret_OutsideStaging_IsRejected()
    {
        // The point of B-8 is that this data must not answer Production at all, even to
        // someone who somehow has the Staging secret.
        var request = BuildRequest(Secret);
        var configuration = BuildConfiguration(secret: Secret, enabled: true);

        Assert.False(StagingTestSupportAuthorization.IsAuthorized(
            request, configuration, new TestHostEnvironment("Production")));
    }

    [Fact(DisplayName = "Correct secret with test support disabled is rejected")]
    public void CorrectSecret_WithTestSupportDisabled_IsRejected()
    {
        var request = BuildRequest(Secret);
        var configuration = BuildConfiguration(secret: Secret, enabled: false);

        Assert.False(StagingTestSupportAuthorization.IsAuthorized(
            request, configuration, new TestHostEnvironment("Staging")));
    }

    private static HttpRequest BuildRequest(string suppliedSecret)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[StagingTestSupportAuthorization.SecretHeaderName] = suppliedSecret;
        return context.Request;
    }

    private static IConfiguration BuildConfiguration(string secret, bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:TestSupport:Enabled"] = enabled ? "true" : "false",
                ["Staging:TestSupport:CleanupSecret"] = secret
            })
            .Build();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "PropertyApi.Architecture.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
