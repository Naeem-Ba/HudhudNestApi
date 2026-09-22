using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Valuation.Interfaces;

namespace HudhudNestApi.Infrastructure.Lookups;

/// <summary>
/// Stage 4 (Office Matching) Level 4 fallback — see <see cref="IGovernorateNeighborProvider"/>
/// for why this exists instead of a GIS/PostGIS/coordinate system.
///
/// BorderMap below is standard, real-world administrative-map adjacency between Syria's 14
/// governorates (the same seeded set as
/// HudhudNestApi.Infrastructure/Persistence/Seeds/GovernoratesSeed.cs, keyed here by the exact
/// NameEn values that file seeds) — sourced the same way that file's own doc comment
/// describes for its governorate/district list: standard reference maps, not GPS-precise,
/// not invented. It intentionally omits foreign neighbors (Lebanon, Jordan, Iraq, Turkey,
/// the Golan) since those never carry an SY Governorate row to resolve to.
///
/// Reuses <see cref="ICommonLookupService.GetGovernoratesAsync"/> (already 10-minute cached)
/// for id ↔ NameEn resolution rather than querying AppDbContext directly — one fewer new
/// dependency, and the governorate catalog this needs is exactly what that service already
/// loads.
/// </summary>
public sealed class GovernorateNeighborProvider : IGovernorateNeighborProvider
{
    private static readonly IReadOnlyDictionary<string, string[]> BorderMap = BuildSymmetricMap(
    [
        ("Damascus", ["Rural Damascus"]),
        ("Rural Damascus", ["Damascus", "Homs", "Quneitra", "Daraa", "As-Suwayda"]),
        ("Aleppo", ["Idlib", "Raqqa", "Hama"]),
        ("Homs", ["Rural Damascus", "Hama", "Tartus", "Deir ez-Zor", "Raqqa", "As-Suwayda"]),
        ("Hama", ["Aleppo", "Homs", "Idlib", "Latakia", "Raqqa"]),
        ("Latakia", ["Tartus", "Hama", "Idlib"]),
        ("Tartus", ["Latakia", "Homs"]),
        ("Al-Hasakah", ["Raqqa", "Deir ez-Zor"]),
        ("Deir ez-Zor", ["Al-Hasakah", "Raqqa", "Homs"]),
        ("Raqqa", ["Aleppo", "Al-Hasakah", "Deir ez-Zor", "Homs", "Hama"]),
        ("Idlib", ["Aleppo", "Hama", "Latakia"]),
        ("Daraa", ["Rural Damascus", "As-Suwayda", "Quneitra"]),
        ("Quneitra", ["Rural Damascus", "Daraa"]),
        ("As-Suwayda", ["Rural Damascus", "Homs", "Daraa"]),
    ]);

    private readonly ICommonLookupService _lookups;

    public GovernorateNeighborProvider(ICommonLookupService lookups)
    {
        _lookups = lookups;
    }

    public async Task<IReadOnlyList<int>> GetNeighborIdsAsync(int governorateId, CancellationToken ct = default)
    {
        var governorates = await _lookups.GetGovernoratesAsync(ct: ct);

        var current = governorates.FirstOrDefault(g => g.Id == governorateId);
        if (current is null || !BorderMap.TryGetValue(current.NameEn, out var neighborNames))
            return Array.Empty<int>();

        return governorates
            .Where(g => neighborNames.Contains(g.NameEn))
            .Select(g => g.Id)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string[]> BuildSymmetricMap(
        (string NameEn, string[] Neighbors)[] pairs)
    {
        var map = new Dictionary<string, HashSet<string>>();

        foreach (var (nameEn, neighbors) in pairs)
        {
            var set = map.TryGetValue(nameEn, out var existing) ? existing : map[nameEn] = [];
            foreach (var neighbor in neighbors)
            {
                set.Add(neighbor);

                var reverseSet = map.TryGetValue(neighbor, out var reverse) ? reverse : map[neighbor] = [];
                reverseSet.Add(nameEn);
            }
        }

        return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }
}
