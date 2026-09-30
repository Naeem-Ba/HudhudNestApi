using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.ShortStay;

public sealed class PricingRuleRepository : IPricingRuleRepository
{
    private readonly AppDbContext _db;
    public PricingRuleRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PricingRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default)
        => await _db.ShortStayPricingRules.Where(r => r.RoomTypeId == roomTypeId).ToListAsync(ct);

    public async Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<PricingRule> rules, CancellationToken ct = default)
    {
        var existing = await _db.ShortStayPricingRules.Where(r => r.RoomTypeId == roomTypeId).ToListAsync(ct);
        _db.ShortStayPricingRules.RemoveRange(existing);
        await _db.ShortStayPricingRules.AddRangeAsync(rules, ct);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class MinimumStayRuleRepository : IMinimumStayRuleRepository
{
    private readonly AppDbContext _db;
    public MinimumStayRuleRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MinimumStayRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default)
        => await _db.ShortStayMinimumStayRules.Where(r => r.RoomTypeId == roomTypeId).ToListAsync(ct);

    public async Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<MinimumStayRule> rules, CancellationToken ct = default)
    {
        var existing = await _db.ShortStayMinimumStayRules.Where(r => r.RoomTypeId == roomTypeId).ToListAsync(ct);
        _db.ShortStayMinimumStayRules.RemoveRange(existing);
        await _db.ShortStayMinimumStayRules.AddRangeAsync(rules, ct);
        await _db.SaveChangesAsync(ct);
    }
}
