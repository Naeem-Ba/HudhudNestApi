using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Auth.Services;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Infrastructure.Auth.Services;

public sealed class PhoneVerificationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PhoneVerificationHostedService> _logger;
    public PhoneVerificationHostedService(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PhoneVerificationHostedService> logger)
    { _scopes = scopes; _clock = clock; _configuration = configuration; _environment = environment; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), _clock);
        do
        {
            if (_configuration.GetValue<bool>("PhoneVerification:ReminderProcessingEnabled"))
            {
                try
                {
                    await ProcessAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A failed tick (database briefly unreachable, migration not applied yet, ...) must not
                    // escape ExecuteAsync: the default BackgroundServiceExceptionBehavior.StopHost would take
                    // the whole API process down. Log it and try again on the next tick.
                    _logger.LogError(ex, "Phone verification reminder processing failed; will retry on the next tick.");
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Guarded by a Postgres advisory lock (BackgroundJobLockKeys.PhoneVerificationReminder):
    /// every deployed instance runs this same hourly timer over the same users, and without
    /// coordination two instances would both page through and double-write reminder
    /// notifications. See B-6 in RELEASE-BLOCKERS-AR.md.
    ///
    /// The lock lives on its own dedicated connection, separate from the per-page AppDbContext
    /// scopes below, because this method opens and disposes a new scope every 100 users —
    /// a session-level lock has to outlive all of them, not any single one.
    /// </summary>
    internal async Task ProcessAsync(CancellationToken ct)
    {
        var connectionString = PostgresConnectionStringResolver.Resolve(_configuration, _environment);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);

        try
        {
            await BackgroundJobLock.TryRunAsync(
                lockConnection,
                BackgroundJobLockKeys.PhoneVerificationReminder,
                nameof(PhoneVerificationHostedService),
                _logger,
                async () =>
                {
                    // Only users with something to do: dates missing (repaired below), inside the reminder
                    // window or past it, or a stored state that no longer matches (e.g. re-verified elsewhere).
                    // Everyone else is Verified with no event due -- used to be read in full every hour.
                    // The ids are taken up front and processed in chunks: paging with Skip over a set the tick
                    // itself changes (a state fixed back to Verified leaves the filter) skipped users.
                    var windowEnd = _clock.GetUtcNow() + PhoneVerificationPolicy.DueSoonWindow;
                    List<Guid> candidateIds;
                    using (var idScope = _scopes.CreateScope())
                    {
                        candidateIds = await idScope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                            .Where(x => x.PhoneLastVerifiedAtUtc != null &&
                                (x.PhoneVerificationDueAtUtc == null || x.PhoneVerificationGraceEndsAtUtc == null ||
                                 x.PhoneVerificationDueAtUtc <= windowEnd ||
                                 x.PhoneVerificationState != PhoneVerificationState.Verified))
                            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
                    }

                    foreach (var chunk in candidateIds.Chunk(100))
                    {
                        ct.ThrowIfCancellationRequested();
                        using var scope = _scopes.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
                        var users = await db.Users.Where(x => chunk.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(ct);
                        var now = _clock.GetUtcNow();
                        foreach (var user in users)
                        {
                            // A verified user without due dates (imported, edited by hand, an older release) used
                            // to throw here and abort the whole tick, so nobody got a reminder until someone fixed
                            // that one row. Derive the dates the way SetVerified would have and repair the row.
                            var due = user.PhoneVerificationDueAtUtc ??=
                                user.PhoneLastVerifiedAtUtc!.Value + PhoneVerificationPolicy.VerificationInterval;
                            var grace = user.PhoneVerificationGraceEndsAtUtc ??= due + PhoneVerificationPolicy.GracePeriod;
                            var state = PhoneVerificationPolicy.Evaluate(due, grace, now);
                            var previousState = user.PhoneVerificationState;
                            user.PhoneVerificationState = state;
                            if (state != previousState)
                            {
                                var action = state switch
                                {
                                    PhoneVerificationState.DueSoon => AuditActions.PhoneVerificationDueSoon,
                                    PhoneVerificationState.GracePeriod => AuditActions.PhoneVerificationGraceStarted,
                                    PhoneVerificationState.Restricted => AuditActions.PhoneVerificationRestricted,
                                    _ => null
                                };
                                if (action is not null)
                                    await audit.LogAsync(user.Id, action, null,
                                        newValue: $"{{\"outcome\":\"transitioned\",\"state\":\"{state}\"}}", ct: ct);
                            }
                            var eventName = EventName(now, due, grace, state);
                            var key = eventName is null ? null : $"{due:yyyyMMdd}:{eventName}";
                            if (key is not null && user.LastPhoneVerificationNotificationKey != key)
                            {
                                db.Notifications.Add(new Notification
                                {
                                    RecipientId = user.Id,
                                    Type = NotificationType.PhoneVerification,
                                    Message = Message(eventName!)
                                });
                                user.LastPhoneVerificationNotificationKey = key;
                            }
                        }
                        await db.SaveChangesAsync(ct);
                    }
                },
                ct);
        }
        finally
        {
            await lockConnection.CloseAsync();
        }
    }

    private static string? EventName(DateTimeOffset now, DateTimeOffset due, DateTimeOffset grace, PhoneVerificationState state)
    {
        var days = (int)Math.Ceiling((due - now).TotalDays);
        if (days is 14 or 7 or 1) return $"Due{days}";
        if (state == PhoneVerificationState.GracePeriod && now < due.AddDays(1)) return "GraceStarted";
        if (now >= grace.AddDays(-1) && now < grace) return "GraceEndsTomorrow";
        return state == PhoneVerificationState.Restricted ? "Restricted" : null;
    }

    private static string Message(string name) => name switch
    {
        "Due14" => "Phone ownership verification is due in 14 days.",
        "Due7" => "Phone ownership verification is due in 7 days.",
        "Due1" => "Phone ownership verification is due tomorrow.",
        "GraceStarted" => "The phone verification grace period has started.",
        "GraceEndsTomorrow" => "The phone verification grace period ends tomorrow.",
        _ => "The account is restricted until phone ownership is verified."
    };
}
