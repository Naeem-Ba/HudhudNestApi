using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Architecture.Tests.Api;

/// <summary>
/// Pins the Production side of Staging/Production isolation: Production must refuse to boot with
/// Staging-shaped configuration, and must not be blocked by anything that merely looks similar.
/// </summary>
public sealed class EnvironmentGuardTests
{
    private const string ProductionDatabaseUrl = "postgresql://user:pw@prod-db.example.com:5432/hudhud_prod";

    [Fact(DisplayName = "Production with production-shaped settings starts")]
    public void Production_WithCleanSettings_Passes() =>
        Assert.Null(Record.Exception(() => Run("Production", ("ConnectionStrings:DefaultConnection", ProductionDatabaseUrl))));

    [Theory(DisplayName = "Production rejects Staging-shaped settings")]
    [InlineData("ConnectionStrings:DefaultConnection", "postgresql://u:p@h.example.com/propertyapi_staging_db")]
    [InlineData("ConnectionStrings:DefaultConnection", "Host=h;Database=propertyapi_staging;Username=u;Password=p")]
    [InlineData("ConnectionStrings:Redis", "propertyapi-staging-redis:6379")]
    [InlineData("Cloudinary:FolderPrefix", "staging")]
    [InlineData("Staging:EnvironmentId", "propertyapi-staging")]
    [InlineData("Staging:TestSupport:Enabled", "true")]
    public void Production_WithStagingShapedSetting_Throws(string key, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Run("Production", (key, value)));
        Assert.Contains("Production refuses to start", ex.Message);
    }

    [Fact(DisplayName = "A staging-looking host or user does not block Production (only the database NAME is checked)")]
    public void Production_StagingWordInHostOnly_Passes() =>
        Assert.Null(Record.Exception(() => Run("Production",
            ("ConnectionStrings:DefaultConnection", "postgresql://staging-user:pw@staging-named-host.example.com/hudhud_prod"))));

    [Theory(DisplayName = "The Production guard is inert outside Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void OtherEnvironments_AreIgnored(string environment) =>
        Assert.Null(Record.Exception(() => Run(environment, ("Staging:TestSupport:Enabled", "true"),
            ("ConnectionStrings:DefaultConnection", "postgresql://u:p@h/x_staging"))));

    private static void Run(string environment, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        ProductionEnvironmentGuard.Validate(configuration, new TestHostEnvironment(environment));
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HudhudNestApi.Architecture.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
