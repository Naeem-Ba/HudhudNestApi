using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Email;

public sealed class SecurityAlertLanguageResolver : ISecurityAlertLanguageResolver
{
    private readonly AppDbContext _db;

    public SecurityAlertLanguageResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetLanguageByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToUpperInvariant();

        var row = await (
            from user in _db.Users.AsNoTracking()
            where user.NormalizedEmail == normalized
            join account in _db.Set<UserAccount>().AsNoTracking() on user.Id equals account.Id
            select new { account.PreferredLanguage, account.PreferredLanguageSetAt })
            .FirstOrDefaultAsync(ct);

        // An account that never chose keeps the wording it always received (English).
        return row is null || row.PreferredLanguageSetAt is null
            ? null
            : SupportedPreferences.NormalizeLanguage(row.PreferredLanguage);
    }
}
