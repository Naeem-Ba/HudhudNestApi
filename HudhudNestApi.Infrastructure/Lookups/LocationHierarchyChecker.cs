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
        if (districtId is { } district && governorateId is { } governorate &&
            !await _db.Districts.AnyAsync(d => d.Id == district && d.GovernorateId == governorate, ct))
            return false;

        if (neighborhoodId is { } neighborhood && districtId is { } parent &&
            !await _db.Neighborhoods.AnyAsync(n => n.Id == neighborhood && n.DistrictId == parent, ct))
            return false;

        return true;
    }
}
