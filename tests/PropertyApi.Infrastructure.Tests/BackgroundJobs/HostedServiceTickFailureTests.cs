using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Infrastructure.Audit;
using PropertyApi.Infrastructure.Auth.Services;
using PropertyApi.Infrastructure.Users;

namespace PropertyApi.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// A BackgroundService that lets an exception escape ExecuteAsync stops the whole host under the
/// default BackgroundServiceExceptionBehavior.StopHost — one failed database call (a Postgres
/// restart during a deploy, a rejected password, a table that is not migrated yet) would take the
/// entire API down. These three timers previously had no per-tick guard, unlike the other hosted
/// services. The failure is provoked deterministically by omitting the connection string, so the
/// sweep throws before any network access.
/// </summary>
public sealed class HostedServiceTickFailureTests
{
    private static readonly IConfiguration EnabledWithoutConnectionString =
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuditLogRetention:Enabled"] = "true",
                ["AuditLogRetention:RetentionDays"] = "365",
                ["PhoneVerification:ReminderProcessingEnabled"] = "true",
            })
            .Build();

    private static IHostEnvironment DevelopmentEnvironment()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Development);
        return environment.Object;
    }

    private static async Task AssertSurvivesFailedFirstTickAsync(BackgroundService service)
    {
        await service.StartAsync(CancellationToken.None);

        // The first tick runs immediately and fails; give it time to do so.
        await Task.Delay(500);

        Assert.NotNull(service.ExecuteTask);
        Assert.False(
            service.ExecuteTask!.IsFaulted,
            "A failed tick must be logged and retried, not allowed to fault ExecuteAsync (which stops the host).");
        Assert.False(service.ExecuteTask.IsCompleted);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public Task AccountDeletionSweep_FailedTick_DoesNotFaultTheService() =>
        AssertSurvivesFailedFirstTickAsync(new AccountDeletionSweepHostedService(
            Mock.Of<IServiceScopeFactory>(),
            TimeProvider.System,
            EnabledWithoutConnectionString,
            DevelopmentEnvironment(),
            NullLogger<AccountDeletionSweepHostedService>.Instance));

    [Fact]
    public Task AuditLogRetention_FailedTick_DoesNotFaultTheService() =>
        AssertSurvivesFailedFirstTickAsync(new AuditLogRetentionHostedService(
            Mock.Of<IServiceScopeFactory>(),
            TimeProvider.System,
            EnabledWithoutConnectionString,
            DevelopmentEnvironment(),
            NullLogger<AuditLogRetentionHostedService>.Instance));

    [Fact]
    public Task PhoneVerification_FailedTick_DoesNotFaultTheService() =>
        AssertSurvivesFailedFirstTickAsync(new PhoneVerificationHostedService(
            Mock.Of<IServiceScopeFactory>(),
            TimeProvider.System,
            EnabledWithoutConnectionString,
            DevelopmentEnvironment(),
            NullLogger<PhoneVerificationHostedService>.Instance));
}
