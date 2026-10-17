using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

/// <summary>
/// The clock of a service request before delivery:
///   1. an inquiry the customer has not confirmed gets one in-app reminder after two days;
///   2. a request nobody touched for a month is closed as Expired (kept, never deleted).
///
/// Both phases are idempotent and derived only from timestamps already in the database (ReminderSentAt,
/// LastActivityAt), so a missed sweep (the free Render instance sleeps at night) only delays them.
/// Pattern mirrors ListingExpiryHostedService: IServiceScopeFactory + a polling loop + a Postgres
/// advisory lock so that two instances never process the same rows.
/// </summary>
public sealed class ServiceRequestLifecycleHostedService : BackgroundService
{
    /// <summary>Both rules are measured in days: a few sweeps a day are plenty and cost one cheap indexed query each.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    /// <summary>A backlog drains over several sweeps instead of one transaction and a flood of notifications.</summary>
    private const int MaxItemsPerPhase = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServiceRequestLifecycleHostedService> _logger;

    public ServiceRequestLifecycleHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceRequestLifecycleHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Service request lifecycle service started. Interval={Interval}, Reminder={Reminder}, Expiry={Expiry}.",
            SweepInterval,
            ServiceRequestLifecyclePolicy.ConfirmReminderAfter,
            ServiceRequestLifecyclePolicy.ExpireAfterInactivity);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service request lifecycle sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>One full sweep. Internal so a test can drive it without waiting on the timer.</summary>
    internal async Task RunOnceAsync(CancellationToken ct, DateTime? utcNow = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.ServiceRequestLifecycle,
                nameof(ServiceRequestLifecycleHostedService),
                _logger,
                async () =>
                {
                    var now = utcNow ?? DateTime.UtcNow;

                    var reminded = await RemindUnconfirmedAsync(db, notifications, now, ct);
                    var expired = await ExpireInactiveAsync(db, notifications, now, ct);

                    if (reminded + expired > 0)
                    {
                        _logger.LogInformation(
                            "Service request lifecycle sweep complete. Reminded={Reminded}, Expired={Expired}.",
                            reminded, expired);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> RemindUnconfirmedAsync(
        AppDbContext db, INotificationService notifications, DateTime now, CancellationToken ct)
    {
        var cutoff = now - ServiceRequestLifecyclePolicy.ConfirmReminderAfter;

        var candidates = await db.ServiceRequests
            .Include(r => r.Property)
            .Where(r => r.Status == ServiceRequestStatus.Inquiry
                && r.ReminderSentAt == null
                && r.CreatedAt <= cutoff)
            .OrderBy(r => r.CreatedAt)
            .Take(MaxItemsPerPhase)
            .ToListAsync(ct);

        foreach (var request in candidates)
        {
            try
            {
                await notifications.NotifyPropertyUpdateAsync(
                    recipientId: request.RequesterId,
                    propertyId: request.PropertyId,
                    propertyTitle: request.Property?.Title ?? "-",
                    type: NotificationType.ServiceRequestConfirmReminder,
                    detail: NotificationText.Of(NotificationKeys.ServiceConfirmReminderDetail, ("number", request.RequestNumber)),
                    relatedEntityId: request.Id,
                    ct: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stamp anyway: a reminder that cannot be delivered must not be retried forever.
                _logger.LogWarning(ex, "Could not send the confirm reminder of service request {ServiceRequestId}.", request.Id);
            }

            request.MarkReminderSent(now);
        }

        await db.SaveChangesAsync(ct);
        return candidates.Count;
    }

    private async Task<int> ExpireInactiveAsync(
        AppDbContext db, INotificationService notifications, DateTime now, CancellationToken ct)
    {
        var cutoff = now - ServiceRequestLifecyclePolicy.ExpireAfterInactivity;

        var candidates = (await db.ServiceRequests
                .Include(r => r.Property)
                .Include(r => r.ServiceProvider)
                .Where(r => r.LastActivityAt <= cutoff
                    && (r.Status == ServiceRequestStatus.Inquiry
                        || r.Status == ServiceRequestStatus.UnderReview
                        || r.Status == ServiceRequestStatus.Accepted
                        || r.Status == ServiceRequestStatus.Scheduled))
                .OrderBy(r => r.LastActivityAt)
                .Take(MaxItemsPerPhase)
                .ToListAsync(ct))
            .Where(r => r.IsExpirable(now))
            .ToList();

        if (candidates.Count == 0)
            return 0;

        var ids = candidates.Select(r => r.Id).ToList();

        // Who spoke last decides who stayed silent: if the customer wrote last (or nobody wrote), the
        // provider never answered; if the provider wrote last, the customer did.
        var lastSender = (await db.ServiceRequestMessages
                .Where(m => ids.Contains(m.ServiceRequestId))
                .GroupBy(m => m.ServiceRequestId)
                .Select(g => new
                {
                    RequestId = g.Key,
                    Sender = g.OrderByDescending(m => m.CreatedAt).Select(m => m.SenderParty).First(),
                })
                .ToListAsync(ct))
            .ToDictionary(x => x.RequestId, x => x.Sender);

        foreach (var request in candidates)
        {
            var from = request.Status;
            var waitingOn = lastSender.TryGetValue(request.Id, out var sender) && sender == ServiceParty.Provider
                ? ServiceParty.Requester
                : ServiceParty.Provider;

            request.Expire(waitingOn, now);

            db.ServiceRequestStatusHistories.Add(
                ServiceRequestStatusHistory.Record(
                    request.Id, from, request.Status, changedByUserId: null,
                    note: "أُغلق تلقائيًا لعدم وجود أي تواصل منذ شهر.", utcNow: now));
        }

        await db.SaveChangesAsync(ct);

        foreach (var request in candidates)
        {
            var detail = NotificationText.Of(NotificationKeys.ServiceExpiredDetail, ("number", request.RequestNumber));

            foreach (var recipient in new[] { request.RequesterId, request.ServiceProvider!.UserId })
            {
                try
                {
                    await notifications.NotifyPropertyUpdateAsync(
                        recipientId: recipient,
                        propertyId: request.PropertyId,
                        propertyTitle: request.Property?.Title ?? "-",
                        type: NotificationType.ServiceRequestExpired,
                        detail: detail,
                        relatedEntityId: request.Id,
                        ct: ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not send the expiry notice of service request {ServiceRequestId}.", request.Id);
                }
            }
        }

        return candidates.Count;
    }
}
