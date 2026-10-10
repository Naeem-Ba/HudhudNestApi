using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Notifications;

public sealed class RecipientLanguageResolver : IRecipientLanguageResolver
{
    private readonly AppDbContext _db;

    public RecipientLanguageResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetLanguageAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return SupportedPreferences.FallbackLanguage;
        }

        // Read the recipient's own row only. The language of whoever triggered the event is never
        // an input here, and neither is anything the HTTP client sent.
        var row = await _db.Set<UserAccount>()
            .AsNoTracking()
            .Where(a => a.Id == userId)
            .Select(a => new { a.PreferredLanguage, a.PreferredLanguageSetAt })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.PreferredLanguageSetAt is null)
        {
            // Never chose: the stored "en" is only the column default (see SupportedPreferences).
            return SupportedPreferences.FallbackLanguage;
        }

        return SupportedPreferences.NormalizeLanguage(row.PreferredLanguage)
               ?? SupportedPreferences.FallbackLanguage;
    }
}
