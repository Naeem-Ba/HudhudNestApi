using Microsoft.EntityFrameworkCore;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

public static class GovernoratesSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        var damascusId = await EnsureGovernorateAsync(
            context,
            nameAr: "دمشق",
            nameEn: "Damascus",
            sortOrder: 1);

        await EnsureGovernorateAsync(context, "ريف دمشق", "Rural Damascus", 2);
        await EnsureGovernorateAsync(context, "حلب", "Aleppo", 3);
        await EnsureGovernorateAsync(context, "حمص", "Homs", 4);
        await EnsureGovernorateAsync(context, "حماة", "Hama", 5);
        await EnsureGovernorateAsync(context, "اللاذقية", "Latakia", 6);
        await EnsureGovernorateAsync(context, "طرطوس", "Tartus", 7);
        await EnsureGovernorateAsync(context, "الحسكة", "Al-Hasakah", 8);
        await EnsureGovernorateAsync(context, "دير الزور", "Deir ez-Zor", 9);
        await EnsureGovernorateAsync(context, "الرقة", "Raqqa", 10);
        await EnsureGovernorateAsync(context, "إدلب", "Idlib", 11);
        await EnsureGovernorateAsync(context, "درعا", "Daraa", 12);
        await EnsureGovernorateAsync(context, "القنيطرة", "Quneitra", 13);
        await EnsureGovernorateAsync(context, "السويداء", "As-Suwayda", 14);

        await EnsureDistrictAsync(context, damascusId, "مزة", "Mazzeh", 1);
        await EnsureDistrictAsync(context, damascusId, "كفر سوسة", "Kafr Sousa", 2);
        await EnsureDistrictAsync(context, damascusId, "المزرعة", "Al-Mazra'a", 3);
        await EnsureDistrictAsync(context, damascusId, "الشعلان", "Al-Sha'lan", 4);
        await EnsureDistrictAsync(context, damascusId, "أبو رمانة", "Abu Rumaneh", 5);
        await EnsureDistrictAsync(context, damascusId, "المالكي", "Al-Malki", 6);
        await EnsureDistrictAsync(context, damascusId, "جرمانا", "Jaramana", 7);
        await EnsureDistrictAsync(context, damascusId, "القدم", "Al-Qadam", 8);

        await context.SaveChangesAsync();
    }

    private static async Task<int> EnsureGovernorateAsync(
        AppDbContext context,
        string nameAr,
        string nameEn,
        int sortOrder)
    {
        var existingId = await context.Governorates
            .Where(g => g.NameAr == nameAr && g.CountryCode == "SY")
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync();

        if (existingId.HasValue)
            return existingId.Value;

        var governorate = Governorate.Create(
            nameAr: nameAr,
            nameEn: nameEn,
            countryCode: "SY",
            sortOrder: sortOrder);

        context.Governorates.Add(governorate);

        await context.SaveChangesAsync();

        return governorate.Id;
    }

    private static async Task EnsureDistrictAsync(
        AppDbContext context,
        int governorateId,
        string nameAr,
        string nameEn,
        int sortOrder)
    {
        var exists = await context.Districts.AnyAsync(d =>
            d.GovernorateId == governorateId &&
            d.NameAr == nameAr);

        if (exists)
            return;

        context.Districts.Add(
            District.Create(
                governorateId: governorateId,
                nameAr: nameAr,
                nameEn: nameEn,
                sortOrder: sortOrder));
    }
}