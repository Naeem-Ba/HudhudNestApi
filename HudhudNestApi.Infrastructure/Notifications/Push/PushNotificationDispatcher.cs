using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Push;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Notifications.Push;

public sealed class PushNotificationDispatcher : IPushNotificationDispatcher
{
    private readonly AppDbContext _db;
    private readonly IPushSender _sender;
    private readonly ILogger<PushNotificationDispatcher> _logger;

    public PushNotificationDispatcher(
        AppDbContext db,
        IPushSender sender,
        ILogger<PushNotificationDispatcher> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid recipientId, PushPayload payload, CancellationToken ct = default)
    {
        try
        {
            var tokens = await _db.DeviceTokens
                .AsNoTracking()
                .Where(t => t.UserId == recipientId)
                .Select(t => new { t.Id, t.Token })
                .ToListAsync(ct);

            if (tokens.Count == 0)
            {
                return;
            }

            var dead = new List<Guid>();
            foreach (var t in tokens)
            {
                var status = await _sender.SendAsync(t.Token, payload, ct);
                if (status == PushSendStatus.InvalidToken)
                {
                    dead.Add(t.Id);
                }
                else if (status == PushSendStatus.Disabled)
                {
                    return; // not configured: no point trying the rest
                }
            }

            if (dead.Count > 0)
            {
                await _db.DeviceTokens.Where(t => dead.Contains(t.Id)).ExecuteDeleteAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never the notification text: it names people and listings.
            _logger.LogWarning(ex, "Push dispatch failed. RecipientId={RecipientId}, NotificationId={NotificationId}",
                recipientId, payload.NotificationId);
        }
    }
}
