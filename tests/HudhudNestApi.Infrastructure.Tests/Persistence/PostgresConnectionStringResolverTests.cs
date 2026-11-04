using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using HudhudNestApi.Infrastructure;

namespace HudhudNestApi.Infrastructure.Tests.Persistence;

/// <summary>
/// QA Phase 18, P18-D07: the Supabase Transaction pooler (port 6543) breaks the session advisory locks the
/// background jobs rely on and prepared statements, so Production must refuse it. Session mode (5432) and the
/// direct connection keep working, and non-Production environments are untouched.
/// </summary>
public sealed class PostgresConnectionStringResolverTests
{
    private const string Ref = "abcdefghijklmnopqrst";

    private static string PoolerUrl(int? port) =>
        $"postgresql://postgres.{Ref}:s3cret-Pass@aws-0-eu-central-1.pooler.supabase.com{(port is null ? "" : ":" + port)}/postgres";

    [Fact(DisplayName = "Production refuses the Supabase transaction pooler port 6543 and says why")]
    public void Production_Rejects_TransactionPoolerPort()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStringResolver.ResolveFromRaw(PoolerUrl(6543), new Env("Production")));

        Assert.Contains("6543", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Transaction mode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("advisory locks", ex.Message, StringComparison.Ordinal);
        Assert.Contains("5432", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Production refuses the transaction pooler in key/value form too")]
    public void Production_Rejects_TransactionPoolerPort_KeyValueForm()
    {
        var raw = $"Host=aws-0-eu-central-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.{Ref};Password=s3cret-Pass;SSL Mode=Require";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStringResolver.ResolveFromRaw(raw, new Env("Production")));

        Assert.Contains("6543", ex.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Production refuses any other pooler port than 5432")]
    [InlineData(6542)]
    [InlineData(5433)]
    [InlineData(443)]
    public void Production_Rejects_OtherPoolerPorts(int port)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStringResolver.ResolveFromRaw(PoolerUrl(port), new Env("Production")));

        Assert.Contains("5432", ex.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Production accepts the session pooler on 5432, explicit or by default")]
    [InlineData(5432)]
    [InlineData(null)]
    public void Production_Accepts_SessionPooler(int? port)
    {
        var resolved = PostgresConnectionStringResolver.ResolveFromRaw(PoolerUrl(port), new Env("Production"));

        var builder = new NpgsqlConnectionStringBuilder(resolved);
        Assert.Equal(5432, builder.Port);
        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.True(builder.MaxPoolSize <= 20, "the pooler cap must still apply");
    }

    [Fact(DisplayName = "Production accepts the Supabase direct connection on 5432")]
    public void Production_Accepts_DirectConnection()
    {
        var raw = $"postgresql://postgres:s3cret-Pass@db.{Ref}.supabase.co:5432/postgres";

        var resolved = PostgresConnectionStringResolver.ResolveFromRaw(raw, new Env("Production"));

        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(resolved).Port);
    }

    [Theory(DisplayName = "Outside Production the pooler port is not policed")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void NonProduction_IsUntouched(string environment)
    {
        var raw = "Host=localhost;Port=6543;Database=hn;Username=postgres;Password=postgres";

        var resolved = PostgresConnectionStringResolver.ResolveFromRaw(raw, new Env(environment));

        Assert.Equal(6543, new NpgsqlConnectionStringBuilder(resolved).Port);
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HudhudNestApi.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
