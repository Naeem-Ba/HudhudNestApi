using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Notifications.Push;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Notifications.Push;

public sealed class DeviceTokenService : IDeviceTokenService
{
    private const int MaxTokensPerUser = 20;

    private readonly AppDbContext _db;

    public DeviceTokenService(AppDbContext db)
    {
        _db = db;
    }

    public async Task RegisterAsync(Guid userId, string token, DevicePlatform platform, CancellationToken ct = default)
    {
        token = token.Trim();
        var now = DateTime.UtcNow;

        var existing = await _db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token, ct);
        if (existing is not null)
        {
            existing.Rebind(userId, platform, now);
        }
        else
        {
            _db.DeviceTokens.Add(DeviceToken.Create(userId, token, platform, now));
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Two concurrent registrations of the same new token: the loser rebinds the winner's row.
            _db.ChangeTracker.Clear();
            var row = await _db.DeviceTokens.FirstAsync(t => t.Token == token, ct);
            row.Rebind(userId, platform, now);
            await _db.SaveChangesAsync(ct);
        }

        // Bound the fan-out per account: keep the most recently seen installs.
        var stale = await _db.DeviceTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastSeenAt)
            .Skip(MaxTokensPerUser)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            _db.DeviceTokens.RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task UnregisterAsync(Guid userId, string token, CancellationToken ct = default)
    {
        token = token.Trim();
        await _db.DeviceTokens
            .Where(t => t.UserId == userId && t.Token == token)
            .ExecuteDeleteAsync(ct);
    }

    public async Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await _db.DeviceTokens
            .Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }
}
