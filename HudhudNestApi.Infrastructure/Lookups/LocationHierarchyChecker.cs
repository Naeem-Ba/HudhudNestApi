using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Lookups;

public sealed class LocationHierarchyChecker : ILocationHierarchyChecker
{
    private readonly AppDbContext _db;

    public LocationHierarchyChecker(AppDbContext db) => _db = db;

    public async Task<bool> IsConsistentAsync(
        int? governorateId, int? districtId, int? neighborhoodId, CancellationToken ct = default)
    {
        // Every id that is present must exist (an unknown one used to reach the foreign key and
        // come back as a 500 - QA Phase 8, P8-D01) and agree with the levels above it.
        if (governorateId is { } governorate &&
            !await _db.Governorates.AnyAsync(g => g.Id == governorate, ct))
            return false;

        if (districtId is { } district &&
            !await _db.Districts.AnyAsync(
                d => d.Id == district && (governorateId == null || d.GovernorateId == governorateId), ct))
            return false;

        if (neighborhoodId is { } neighborhood &&
            !await _db.Neighborhoods.AnyAsync(
                n => n.Id == neighborhood && (districtId == null || n.DistrictId == districtId), ct))
            return false;

        return true;
    }
}
