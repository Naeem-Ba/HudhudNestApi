using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Lookups;

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
