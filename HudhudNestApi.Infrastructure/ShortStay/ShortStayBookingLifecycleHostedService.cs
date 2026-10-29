using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.ShortStay;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.ShortStay;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.ShortStay;

/// <summary>
/// Ends short-stay bookings nobody confirmed in time (QA Phase 9, P9-D04; owner rule 2026-10-18: three days from
/// booking to confirmation, one week at the most). An expired booking releases the unit's dates and both sides
/// are told. Booking.MarkExpired existed but nothing ever called it, so an unanswered request or an approval
/// nobody confirmed held the unit for ever.
///
/// Idempotent and derived only from timestamps already in the database, so a missed sweep (the free Render
/// instance sleeps) only delays it. Pattern mirrors ServiceRequestLifecycleHostedService: scope factory, polling
/// loop, one Postgres advisory lock so two instances never sweep together. Each booking is also handled under the
/// same per-unit lock as approve/confirm/create, so the sweep and a host acting at the same second cannot both win.
/// </summary>
public sealed class ShortStayBookingLifecycleHostedService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    /// <summary>A backlog drains over several sweeps instead of one transaction and a flood of notifications.</summary>
    private const int MaxItemsPerSweep = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortStayBookingLifecycleHostedService> _logger;

    public ShortStayBookingLifecycleHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortStayBookingLifecycleHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Short-stay booking lifecycle service started. Interval={Interval}, PendingWindow={Pending}, ConfirmationWindow={Confirmation}, MaxOpen={MaxOpen}.",
            SweepInterval,
            ShortStayBookingPolicy.PendingResponseWindow,
            ShortStayBookingPolicy.ConfirmationWindow,
            ShortStayBookingPolicy.MaxOpenWindow);

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
                _logger.LogError(ex, "Short-stay booking lifecycle sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>One full sweep. Internal so a test can drive it without waiting on the timer.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct, DateTime? utcNow = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var expired = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.ShortStayBookingLifecycle,
                nameof(ShortStayBookingLifecycleHostedService),
                _logger,
                async () =>
                {
                    expired = await ExpireOverdueAsync(db, notifications, utcNow ?? DateTime.UtcNow, ct);
                    if (expired > 0)
                        _logger.LogInformation("Short-stay booking lifecycle sweep complete. Expired={Expired}.", expired);
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return expired;
    }

    private async Task<int> ExpireOverdueAsync(
        AppDbContext db, INotificationService notifications, DateTime now, CancellationToken ct)
    {
        var pendingCutoff = now - ShortStayBookingPolicy.PendingResponseWindow;
        var confirmationCutoff = now - ShortStayBookingPolicy.ConfirmationWindow;
        var hardCutoff = now - ShortStayBookingPolicy.MaxOpenWindow;

        // A coarse filter in SQL; Booking.IsOverdue is the single definition and decides below.
        var candidateIds = await db.ShortStayBookings
            .Where(b => (b.Status == BookingStatus.Pending && b.CreatedAt <= pendingCutoff)
                || ((b.Status == BookingStatus.Approved || b.Status == BookingStatus.DepositPaid)
                    && (b.RespondedAt ?? b.CreatedAt) <= confirmationCutoff)
                || ((b.Status == BookingStatus.Pending || b.Status == BookingStatus.Approved
                        || b.Status == BookingStatus.DepositPaid) && b.CreatedAt <= hardCutoff))
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .Take(MaxItemsPerSweep)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var bookingId in candidateIds)
        {
            try
            {
                if (await ExpireOneAsync(db, notifications, bookingId, now, ct))
                    expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not expire short-stay booking {BookingId}; it is retried on the next sweep.", bookingId);
                db.ChangeTracker.Clear();
            }
        }

        return expired;
    }

    private async Task<bool> ExpireOneAsync(
        AppDbContext db, INotificationService notifications, Guid bookingId, DateTime now, CancellationToken ct)
    {
        Booking booking;
        ShortStayListing listing;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var unitId = await db.ShortStayBookings.Where(b => b.Id == bookingId).Select(b => b.UnitId).FirstOrDefaultAsync(ct);
            if (unitId == Guid.Empty)
                return false;

            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})", new object[] { ShortStayBookingLock.ForUnit(unitId) }, ct);

            // Read again under the lock: the host may have confirmed it a moment ago.
            booking = await db.ShortStayBookings
                .IgnoreQueryFilters()
                .Include(b => b.Unit).ThenInclude(u => u!.RoomType).ThenInclude(rt => rt.ShortStayListing)
                .FirstAsync(b => b.Id == bookingId, ct);
            await db.Entry(booking).ReloadAsync(ct);

            if (!booking.IsOverdue(now))
                return false;

            booking.MarkExpired();

            var ranges = await db.UnitBookingRanges
                .Where(r => r.BookingId == bookingId && r.Status != UnitRangeStatus.Released)
                .ToListAsync(ct);
            foreach (var range in ranges)
                range.Status = UnitRangeStatus.Released;

            listing = booking.Unit!.RoomType.ShortStayListing;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Committed; telling people is best-effort.
        foreach (var (recipient, key) in new[]
                 {
                     (booking.GuestId, NotificationKeys.ShortStayExpiredGuestDetail),
                     (listing.OwnerId, NotificationKeys.ShortStayExpiredHostDetail),
                 })
        {
            try
            {
                await notifications.NotifyShortStayBookingUpdateAsync(
                    recipient, booking.Id, listing.Title,
                    NotificationType.ShortStayBookingCancelled,
                    NotificationText.Of(key, ("title", listing.Title)), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not send the expiry notice of short-stay booking {BookingId}.", booking.Id);
            }
        }

        return true;
    }
}
