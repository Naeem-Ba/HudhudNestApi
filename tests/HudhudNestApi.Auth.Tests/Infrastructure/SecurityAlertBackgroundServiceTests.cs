using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Infrastructure.Email;

namespace HudhudNestApi.Auth.Tests.Infrastructure;

[Trait("Category", "Security")]
public sealed class SecurityAlertBackgroundServiceTests
{
    /// <summary>
    /// Records what was sent, and proves it was resolved from a scope the worker created
    /// rather than one belonging to a request that has already ended.
    /// </summary>
    private sealed class RecordingAlertService : ISecurityAlertService
    {
        private readonly RecordedAlerts _recorded;
        private readonly bool _throwOnSend;

        public RecordingAlertService(RecordedAlerts recorded, bool throwOnSend)
        {
            _recorded = recorded;
            _throwOnSend = throwOnSend;
        }

        public Task SendFailedLoginAttemptAlertAsync(
            string userEmail, string userName, int failedAttemptCount,
            string? ipAddress, DateTime attemptTimeUtc, CancellationToken ct = default)
            => Record($"failed:{userEmail}:{failedAttemptCount}");

        public Task SendAccountLockedAlertAsync(
            string userEmail, string userName, DateTime? lockoutEnd,
            string? ipAddress, CancellationToken ct = default)
            => Record($"locked:{userEmail}");

        public Task SendPasswordChangeRequestAsync(
            string userEmail, string userName, string passwordResetLink,
            int failedAttemptCount, string? ipAddress, CancellationToken ct = default)
            => Record($"change-request:{userEmail}");

        public Task SendPasswordChangedConfirmationAsync(
            string userEmail, string userName, CancellationToken ct = default)
            => Record($"changed:{userEmail}");

        private Task Record(string entry)
        {
            _recorded.Add(entry);

            return _throwOnSend
                ? Task.FromException(new InvalidOperationException("SMTP is down."))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordedAlerts
    {
        private readonly List<string> _entries = [];
        private readonly Lock_ _gate = new();

        public int ScopesCreated;

        public void Add(string entry)
        {
            lock (_gate) { _entries.Add(entry); }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate) { return _entries.ToArray(); }
        }

        public async Task<bool> WaitForCountAsync(int expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Snapshot().Count >= expected) return true;
                await Task.Delay(10);
            }

            return Snapshot().Count >= expected;
        }

        private sealed class Lock_;
    }

    private static (SecurityAlertBackgroundService Service, RecordedAlerts Recorded) Create(
        bool throwOnSend = false)
    {
        var recorded = new RecordedAlerts();

        var services = new ServiceCollection();
        services.AddScoped<ISecurityAlertService>(_ =>
        {
            Interlocked.Increment(ref recorded.ScopesCreated);
            return new RecordingAlertService(recorded, throwOnSend);
        });

        var provider = services.BuildServiceProvider();

        var service = new SecurityAlertBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SecurityAlertBackgroundService>.Instance);

        return (service, recorded);
    }

    [Fact(DisplayName = "Enqueue returns immediately and the worker delivers out of band")]
    public async Task Enqueue_Should_Deliver_Through_TheWorker()
    {
        var (service, recorded) = Create();
        await service.StartAsync(CancellationToken.None);

        service.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.AccountLocked,
            UserEmail: "locked@example.com",
            UserName: "locked"));

        service.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.FailedLoginAttempt,
            UserEmail: "failed@example.com",
            UserName: "failed",
            FailedAttemptCount: 3));

        Assert.True(
            await recorded.WaitForCountAsync(2, TimeSpan.FromSeconds(5)),
            "The worker did not deliver both queued alerts.");

        await service.StopAsync(CancellationToken.None);

        Assert.Contains("locked:locked@example.com", recorded.Snapshot());
        Assert.Contains("failed:failed@example.com:3", recorded.Snapshot());
    }

    [Fact(DisplayName = "Each alert is delivered from its own DI scope")]
    public async Task Worker_Should_Create_AScope_PerAlert()
    {
        // This is the reason the dispatcher exists. The old code kept a scoped
        // ISecurityAlertService alive past the end of the request scope that produced it;
        // the worker instead resolves a fresh one whose lifetime it controls.
        var (service, recorded) = Create();
        await service.StartAsync(CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            service.Enqueue(new SecurityAlertRequest(
                SecurityAlertKind.AccountLocked,
                UserEmail: $"user{i}@example.com",
                UserName: $"user{i}"));
        }

        Assert.True(await recorded.WaitForCountAsync(3, TimeSpan.FromSeconds(5)));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(3, Volatile.Read(ref recorded.ScopesCreated));
    }

    [Fact(DisplayName = "A failing send is logged and does not stop the worker")]
    public async Task Worker_Should_Survive_AFailingSend()
    {
        // Previously a throwing send produced an unobserved task exception and the
        // failure was invisible. The worker must absorb it and keep draining the queue.
        var (service, recorded) = Create(throwOnSend: true);
        await service.StartAsync(CancellationToken.None);

        service.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.AccountLocked, "first@example.com", "first"));
        service.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.AccountLocked, "second@example.com", "second"));

        Assert.True(
            await recorded.WaitForCountAsync(2, TimeSpan.FromSeconds(5)),
            "The worker stopped after the first failing send instead of continuing.");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact(DisplayName = "Enqueue after shutdown does not throw")]
    public async Task Enqueue_AfterShutdown_Should_Not_Throw()
    {
        // Login must never fail because of the mail path, including during shutdown.
        var (service, _) = Create();
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var exception = Record.Exception(() => service.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.AccountLocked, "late@example.com", "late")));

        Assert.Null(exception);
    }
}
