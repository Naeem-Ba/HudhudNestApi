using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Architecture.Tests.Api;

/// <summary>
/// Pins the Staging side of Staging/Production isolation for the database: the isolation marker
/// may identify the Staging database by name, user or host (a Supabase project ref lives in the
/// user and host, never in the fixed "postgres" database name), and a marker that matches every
/// database is refused.
/// </summary>
public sealed class StagingEnvironmentGuardTests
{
    private const string StagingRef = "stagingref123";

    [Theory(DisplayName = "Staging starts when the marker is in the database name, user or host")]
    [InlineData("Host=h.example.com;Database=hudhudnest_staging;Username=u;Password=p", "staging")]
    [InlineData("Host=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres." + StagingRef + ";Password=p", StagingRef)]
    [InlineData("Host=db." + StagingRef + ".supabase.co;Database=postgres;Username=postgres;Password=p", StagingRef)]
    [InlineData("postgresql://postgres." + StagingRef + ":p@aws-0-eu-central-1.pooler.supabase.com:5432/postgres", StagingRef)]
    public void Staging_MarkerInDatabaseUserOrHost_Passes(string connection, string marker) =>
        Assert.Null(Record.Exception(() => Run(connection, marker)));

    [Theory(DisplayName = "Staging refuses a database the marker does not identify")]
    [InlineData("Host=aws-0-eu-central-1.pooler.supabase.com;Database=postgres;Username=postgres.prodref456;Password=p")]
    [InlineData("postgresql://postgres.prodref456:p@aws-0-eu-central-1.pooler.supabase.com:5432/postgres")]
    [InlineData("Host=prod-db.example.com;Database=hudhud_prod;Username=u;Password=p")]
    public void Staging_MarkerAbsent_Throws(string connection)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Run(connection, StagingRef));
        Assert.Contains("isolation marker", ex.Message);
    }

    [Theory(DisplayName = "Staging refuses 'postgres' as the database marker")]
    [InlineData("postgres")]
    [InlineData(" Postgres ")]
    public void Staging_GenericPostgresMarker_Throws(string marker)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Run(
            "Host=db." + StagingRef + ".supabase.co;Database=postgres;Username=postgres;Password=p", marker));
        Assert.Contains("matches every database", ex.Message);
    }

    private static void Run(string connection, string marker)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Deployment:CommitSha"] = "abc123",
            ["Deployment:Version"] = "1.0.0",
            ["Deployment:PublicBaseUrl"] = "https://staging-api.example.com",
            ["Deployment:ProductionBaseUrl"] = "https://api.example.com",
            ["Staging:EnvironmentId"] = "hudhudnest-staging",
            ["Staging:DatabaseNameMarker"] = marker,
            ["Staging:RedisIsolationMarker"] = "staging-redis",
            ["Staging:StorageIsolationMarker"] = "staging-storage",
            ["Staging:ExternalNotificationsDisabled"] = "true",
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["ConnectionStrings:Redis"] = "staging-redis:6379",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        StagingEnvironmentGuard.Validate(configuration, new TestHostEnvironment("Staging"));
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HudhudNestApi.Architecture.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
