using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Auth.Services;

/// <summary>
/// Deletes sign-in rows that can no longer be used (DB security audit 2026-10-29). Nothing removed
/// them since the old OtpCleanupHostedService went with the OtpCodes table, so the tables kept
/// growing with personal data:
/// - PhoneOtpChallenges hold the phone number in clear text. The workflow only reads challenges
///   from the last hour (send rate limit) or by id while the code is valid, so a row one day past
///   its expiry is never read again.
/// - RefreshTokens keep the client IP addresses. An expired token is refused whatever its row
///   says, so a row 30 days past its expiry only matters as history.
/// - ContactMessages and Leads keep the sender's IP address. No code reads it back, so it is
///   cleared after DataRetention:IpAddressDays (default 90, 0 or less keeps it). Only the IP
///   column changes; the message or lead stays.
/// Postgres advisory lock (BackgroundJobLockKeys.OtpCleanup): one instance sweeps at a time.
/// </summary>
public sealed class AuthDataRetentionHostedService : BackgroundService
{
    internal static readonly TimeSpan OtpChallengeGrace = TimeSpan.FromDays(1);
    internal static readonly TimeSpan RefreshTokenGrace = TimeSpan.FromDays(30);

    public const string IpAddressDaysKey = "DataRetention:IpAddressDays";
    internal const int DefaultIpAddressDays = 90;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private const int BatchSize = 5000;
    private const int MaxBatchesPerSweep = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthDataRetentionHostedService> _logger;
    private readonly int _ipAddressDays;

    public AuthDataRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthDataRetentionHostedService> logger,
        IConfiguration? configuration = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _ipAddressDays = configuration?.GetValue<int?>(IpAddressDaysKey) ?? DefaultIpAddressDays;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auth data retention sweep failed.");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task<(int OtpChallenges, int RefreshTokens, int IpAddresses)> RunOnceAsync(
        CancellationToken ct, DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var otpCutoff = now - OtpChallengeGrace;
        var tokenCutoff = (now - RefreshTokenGrace).UtcDateTime;
        var ipCutoff = now.UtcDateTime.AddDays(-_ipAddressDays);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var otpRemoved = 0;
        var tokensRemoved = 0;
        var ipsCleared = 0;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.OtpCleanup,
                nameof(AuthDataRetentionHostedService),
                _logger,
                async () =>
                {
                    for (var i = 0; i < MaxBatchesPerSweep; i++)
                    {
                        var ids = db.PhoneOtpChallenges
                            .Where(c => c.ExpiresAtUtc < otpCutoff)
                            .OrderBy(c => c.ExpiresAtUtc)
                            .Select(c => c.Id)
                            .Take(BatchSize);
                        var n = await db.PhoneOtpChallenges.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(ct);
                        otpRemoved += n;
                        if (n < BatchSize)
                            break;
                    }

                    for (var i = 0; i < MaxBatchesPerSweep; i++)
                    {
                        // IgnoreQueryFilters: a deleted account's old tokens go too.
                        var ids = db.RefreshTokens.IgnoreQueryFilters()
                            .Where(t => t.ExpiresAt < tokenCutoff)
                            .OrderBy(t => t.ExpiresAt)
                            .Select(t => t.Id)
                            .Take(BatchSize);
                        var n = await db.RefreshTokens.IgnoreQueryFilters()
                            .Where(t => ids.Contains(t.Id))
                            .ExecuteDeleteAsync(ct);
                        tokensRemoved += n;
                        if (n < BatchSize)
                            break;
                    }

                    if (_ipAddressDays > 0)
                    {
                        ipsCleared += await ClearOldIpAddressesAsync(db, ipCutoff, ct);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (otpRemoved > 0 || tokensRemoved > 0 || ipsCleared > 0)
            _logger.LogInformation(
                "Auth data retention removed {OtpCount} expired OTP challenges and {TokenCount} expired refresh tokens, and cleared {IpCount} old IP addresses.",
                otpRemoved, tokensRemoved, ipsCleared);

        return (otpRemoved, tokensRemoved, ipsCleared);
    }

    private static async Task<int> ClearOldIpAddressesAsync(AppDbContext db, DateTime cutoff, CancellationToken ct)
    {
        var cleared = 0;
        for (var i = 0; i < MaxBatchesPerSweep; i++)
        {
            // IgnoreQueryFilters: soft-deleted rows lose their IP too.
            var ids = db.ContactMessages.IgnoreQueryFilters()
                .Where(m => m.IpAddress != null && m.CreatedAt < cutoff)
                .Select(m => m.Id)
                .Take(BatchSize);
            var n = await db.ContactMessages.IgnoreQueryFilters()
                .Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.IpAddress, (string?)null), ct);
            cleared += n;
            if (n < BatchSize)
                break;
        }

        for (var i = 0; i < MaxBatchesPerSweep; i++)
        {
            var ids = db.Leads.IgnoreQueryFilters()
                .Where(l => l.IpAddress != null && l.CreatedAt < cutoff)
                .Select(l => l.Id)
                .Take(BatchSize);
            var n = await db.Leads.IgnoreQueryFilters()
                .Where(l => ids.Contains(l.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.IpAddress, (string?)null), ct);
            cleared += n;
            if (n < BatchSize)
                break;
        }

        return cleared;
    }
}
