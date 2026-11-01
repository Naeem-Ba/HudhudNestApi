using HudhudNestApi.Application.Referrals;
using HudhudNestApi.Domain.Referrals.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Infrastructure.Referrals;

public sealed class ReferralStore : IReferralStore
{
    private readonly AppDbContext _db;
    private readonly ReferralOptions _options;
    private readonly ILogger<ReferralStore> _logger;

    public ReferralStore(AppDbContext db, IOptions<ReferralOptions> options, ILogger<ReferralStore> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReferralCode> GetOrCreateCodeAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        var existing = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing is not null)
            return existing;

        // A collision on 31^8 codes is very unlikely; retry a few times, and a concurrent first request of the
        // same user is resolved by the primary key (the loser reads the winner's code).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = ReferralCode.Generate(userId, nowUtc);
            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ReferralCodes" ("UserId", "Code", "CreatedAt")
                VALUES ({code.UserId}, {code.Code}, {code.CreatedAt})
                ON CONFLICT DO NOTHING
                """, ct);
            var row = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
            if (row is not null)
                return row;
            if (inserted == 0)
                continue; // the code itself collided with another user's
        }

        throw new InvalidOperationException("Could not create a referral code.");
    }

    public async Task AttachAtRegistrationAsync(Guid newUserId, string? code, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            var normalized = ReferralCode.Normalize(code);
            if (!_options.Enabled || normalized is null)
                return;

            var owner = await _db.ReferralCodes.AsNoTracking()
                .Where(c => c.Code == normalized)
                .Select(c => (Guid?)c.UserId)
                .FirstOrDefaultAsync(ct);
            if (owner is null || owner == newUserId)
                return;

            var referral = Referral.Create(owner.Value, newUserId, normalized, nowUtc);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Referrals" ("Id", "ReferrerUserId", "ReferredUserId", "Code", "Status", "CreatedAt")
                VALUES ({referral.Id}, {referral.ReferrerUserId}, {referral.ReferredUserId}, {referral.Code}, {(int)referral.Status}, {referral.CreatedAt})
                ON CONFLICT ("ReferredUserId") DO NOTHING
                """, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Referral for new account {UserId} was not recorded.", newUserId);
        }
    }

    public async Task<IReadOnlyList<Referral>> GetByReferrerAsync(Guid referrerUserId, int take, CancellationToken ct) =>
        await _db.Referrals.AsNoTracking()
            .Where(r => r.ReferrerUserId == referrerUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<AdminReferralPage> GetAdminPageAsync(ReferralStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Referrals.AsNoTracking();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminReferralDto(
                r.Id,
                r.ReferrerUserId,
                _db.UserAccounts.Where(u => u.Id == r.ReferrerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "",
                r.ReferredUserId,
                _db.UserAccounts.Where(u => u.Id == r.ReferredUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "",
                r.Code,
                r.Status.ToString(),
                r.Reason,
                r.CreatedAt,
                r.DecidedAt,
                r.RewardedAt))
            .ToListAsync(ct);
        return new AdminReferralPage(items, total);
    }

    public Task<Referral?> GetAsync(Guid id, CancellationToken ct) => _db.Referrals.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task SaveAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
