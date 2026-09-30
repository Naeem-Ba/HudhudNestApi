using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Infrastructure.Media;

namespace HudhudNestApi.Auth.Tests.Infrastructure;

/// <summary>
/// Staging shares Production's Cloudinary cloud, so the upload folder root is the only thing
/// keeping Staging test uploads out of Production's folders.
/// </summary>
public sealed class MediaFolderPrefixTests
{
    [Fact]
    public void Staging_defaults_to_staging_root_when_nothing_is_configured() =>
        Assert.Equal("staging", MediaInfrastructureRegistration.ResolveFolderPrefix(null, new Env("Staging")));

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Other_environments_have_no_prefix_by_default(string environment) =>
        Assert.Null(MediaInfrastructureRegistration.ResolveFolderPrefix("  ", new Env(environment)));

    [Fact]
    public void Explicit_prefix_wins_and_is_trimmed() =>
        Assert.Equal("qa/team", MediaInfrastructureRegistration.ResolveFolderPrefix(" /qa/team/ ", new Env("Staging")));

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
