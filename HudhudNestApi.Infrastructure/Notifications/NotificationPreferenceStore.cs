using Microsoft.EntityFrameworkCore;
using Npgsql;
using HudhudNestApi.Application.Notifications.Preferences;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Notifications;

public sealed class NotificationPreferenceStore : INotificationPreferenceStore
{
    private readonly AppDbContext _db;

    public NotificationPreferenceStore(AppDbContext db)
    {
        _db = db;
    }

    public Task<NotificationPreference?> FindAsync(Guid userId, CancellationToken ct = default) =>
        _db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task SaveAsync(NotificationPreference preference, CancellationToken ct = default)
    {
        var tracked = await _db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == preference.UserId, ct);
        if (tracked is null)
        {
            _db.NotificationPreferences.Add(preference);
        }
        else
        {
            _db.Entry(tracked).CurrentValues.SetValues(preference);
            tracked.MutedCategories = preference.MutedCategories;
            tracked.PushMutedCategories = preference.PushMutedCategories;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (tracked is null
                                           && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two first saves raced: the other one won; update its row with this one's values.
            _db.ChangeTracker.Clear();
            var row = await _db.NotificationPreferences.FirstAsync(p => p.UserId == preference.UserId, ct);
            _db.Entry(row).CurrentValues.SetValues(preference);
            row.MutedCategories = preference.MutedCategories;
            row.PushMutedCategories = preference.PushMutedCategories;
            await _db.SaveChangesAsync(ct);
        }
    }
}
