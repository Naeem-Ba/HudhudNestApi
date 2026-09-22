using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Infrastructure.Audit;
using Xunit;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Audit;

/// <summary>
/// Covers the privacy fix in docs/privacy/privacy-gaps.md (P1): AuditLogs previously had no
/// retention/cleanup mechanism at all. These tests only cover the guard clauses that must
/// hold BEFORE any database connection is opened — whether the sweep actually deletes rows
/// once enabled needs a real Postgres server (like the rest of this codebase's
/// BackgroundJobLock-guarded sweeps; see AdvisoryLockConcurrencyTests) and is out of scope
/// for a plain unit test.
/// </summary>
public sealed class AuditLogRetentionHostedServiceTests
{
    [Fact]
    public async Task SweepAsync_WhenRetentionDaysIsNotConfigured_ReturnsZero_WithoutTouchingTheDatabase()
    {
        var configuration = BuildConfiguration(retentionDays: null);
        var sut = BuildSut(configuration, out var scopeFactory);

        var deleted = await sut.SweepAsync(CancellationToken.None);

        Assert.Equal(0, deleted);

        // The guard must return before ever asking for a scope (and therefore an
        // AppDbContext/Postgres connection) -- misconfiguration must fail safe, not fail by
        // touching the database with an unintended cutoff.
        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SweepAsync_WhenRetentionDaysIsNotPositive_ReturnsZero_WithoutTouchingTheDatabase(
        int retentionDays)
    {
        var configuration = BuildConfiguration(retentionDays);
        var sut = BuildSut(configuration, out var scopeFactory);

        var deleted = await sut.SweepAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
    }

    private static IConfiguration BuildConfiguration(int? retentionDays)
    {
        var values = new Dictionary<string, string?>
        {
            ["AuditLogRetention:Enabled"] = "true"
        };

        if (retentionDays.HasValue)
        {
            values["AuditLogRetention:RetentionDays"] = retentionDays.Value.ToString();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static AuditLogRetentionHostedService BuildSut(
        IConfiguration configuration,
        out Mock<IServiceScopeFactory> scopeFactory)
    {
        scopeFactory = new Mock<IServiceScopeFactory>();

        return new AuditLogRetentionHostedService(
            scopeFactory.Object,
            TimeProvider.System,
            configuration,
            Mock.Of<IHostEnvironment>(),
            NullLogger<AuditLogRetentionHostedService>.Instance);
    }
}
