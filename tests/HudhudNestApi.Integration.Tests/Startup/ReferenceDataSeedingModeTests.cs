using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Integration.Tests.Startup;

/// <summary>
/// Reference-data seeding must not block Kestrel from listening in Production/Staging: a slow
/// database used to keep <c>/health/live</c> at 503 for the whole seed (or indefinitely).
/// </summary>
public sealed class ReferenceDataSeedingModeTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static IConfiguration Config(string? background = null)
    {
        var values = new Dictionary<string, string?>();
        if (background is not null)
        {
            values[DatabaseSeedingExtensions.BackgroundConfigurationKey] = background;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory(DisplayName = "Production and Staging seed in the background by default")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ShouldSeedInBackground_ProductionLike_DefaultsToTrue(string environment) =>
        Assert.True(DatabaseSeedingExtensions.ShouldSeedInBackground(Config(), Env(environment)));

    [Theory(DisplayName = "Development, Testing and CI keep seeding inline")]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("CI")]
    public void ShouldSeedInBackground_OtherEnvironments_DefaultsToFalse(string environment) =>
        Assert.False(DatabaseSeedingExtensions.ShouldSeedInBackground(Config(), Env(environment)));

    [Fact(DisplayName = "The configuration key overrides the environment default in both directions")]
    public void ShouldSeedInBackground_ConfigurationOverridesDefault()
    {
        Assert.False(DatabaseSeedingExtensions.ShouldSeedInBackground(Config("false"), Env("Production")));
        Assert.True(DatabaseSeedingExtensions.ShouldSeedInBackground(Config("true"), Env("Development")));
    }
}
