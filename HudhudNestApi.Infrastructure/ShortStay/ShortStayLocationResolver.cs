using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.ShortStay;

public sealed class ShortStayLocationResolver : IShortStayLocationResolver
{
    private readonly AppDbContext _db;
    public ShortStayLocationResolver(AppDbContext db) => _db = db;

    public async Task<string?> ResolveCityAsync(
        int? governorateId, int? districtId, int? neighborhoodId, string? fallbackCity, CancellationToken ct)
    {
        if (districtId.HasValue && !governorateId.HasValue)
            throw Invalid(nameof(governorateId), "اختر المحافظة قبل اختيار المنطقة.");

        if (neighborhoodId.HasValue && !districtId.HasValue)
            throw Invalid(nameof(districtId), "اختر المنطقة قبل اختيار الحي.");

        if (!governorateId.HasValue)
            return string.IsNullOrWhiteSpace(fallbackCity) ? null : fallbackCity.Trim();

        var governorate = await _db.Governorates.AsNoTracking()
            .Where(g => g.Id == governorateId.Value && g.IsActive)
            .Select(g => new { g.NameAr })
            .FirstOrDefaultAsync(ct)
            ?? throw Invalid(nameof(governorateId), "المحافظة المختارة غير موجودة.");

        if (districtId.HasValue)
        {
            var districtOk = await _db.Districts.AsNoTracking()
                .AnyAsync(d => d.Id == districtId.Value && d.GovernorateId == governorateId.Value && d.IsActive, ct);
            if (!districtOk)
                throw Invalid(nameof(districtId), "المنطقة المختارة لا تتبع هذه المحافظة.");
        }

        if (neighborhoodId.HasValue)
        {
            var neighborhoodOk = await _db.Neighborhoods.AsNoTracking()
                .AnyAsync(n => n.Id == neighborhoodId.Value && n.DistrictId == districtId!.Value && n.IsActive, ct);
            if (!neighborhoodOk)
                throw Invalid(nameof(neighborhoodId), "الحي المختار لا يتبع هذه المنطقة.");
        }

        return governorate.NameAr;
    }

    private static ValidationException Invalid(string field, string message)
        => new(field, message);
}
