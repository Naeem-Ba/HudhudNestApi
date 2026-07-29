using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PropertyApi.Infrastructure.Auth.Services;

namespace PropertyApi.Auth.Tests.Infrastructure;

public sealed class StagingTestSupportPolicyTests
{
    [Fact]
    public void Enabled_in_staging_when_explicitly_configured()
    {
        var configuration = Configuration(true);
        var environment = new TestEnvironment(Environments.Staging);

        Assert.True(StagingTestSupportPolicy.IsEnabled(environment, configuration));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Disabled_outside_staging_even_when_flag_is_true(string environmentName)
    {
        var configuration = Configuration(true);
        var environment = new TestEnvironment(environmentName);

        Assert.False(StagingTestSupportPolicy.IsEnabled(environment, configuration));
    }

    [Fact]
    public void Disabled_by_default_in_staging()
    {
        var environment = new TestEnvironment(Environments.Staging);

        Assert.False(StagingTestSupportPolicy.IsEnabled(environment, Configuration(false)));
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:TestSupport:Enabled"] = enabled.ToString()
            })
            .Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "PropertyApi.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
