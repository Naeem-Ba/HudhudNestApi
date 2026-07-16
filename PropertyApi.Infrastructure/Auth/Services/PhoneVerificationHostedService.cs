using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class PhoneVerificationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    public PhoneVerificationHostedService(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration)
    { _scopes = scopes; _clock = clock; _configuration = configuration; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), _clock);
        do
        {
            if (_configuration.GetValue<bool>("PhoneVerification:ReminderProcessingEnabled"))
                await ProcessAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ProcessAsync(CancellationToken ct)
    {
        var offset = 0;
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            var users = await db.Users.Where(x => x.PhoneLastVerifiedAtUtc != null)
                .OrderBy(x => x.Id).Skip(offset).Take(100).ToListAsync(ct);
            if (users.Count == 0) break;
            var now = _clock.GetUtcNow();
            foreach (var user in users)
            {
                var due = user.PhoneVerificationDueAtUtc!.Value;
                var grace = user.PhoneVerificationGraceEndsAtUtc!.Value;
                var state = now >= grace ? PhoneVerificationState.Restricted : now >= due
                    ? PhoneVerificationState.GracePeriod : now >= due.AddDays(-14)
                        ? PhoneVerificationState.DueSoon : PhoneVerificationState.Verified;
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
            offset += users.Count;
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
