using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

public static class PropertyTypesSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        await EnsureAsync(context, "apartment", "شقة سكنية", "Apartment", "Residential", "apartment", 1);
        await EnsureAsync(context, "hotel-apartment", "شقة فندقية", "Hotel Apartment", "Residential", "hotel-apt", 2);
        await EnsureAsync(context, "villa", "فيلا", "Villa", "Residential", "villa", 3);
        await EnsureAsync(context, "house", "منزل مستقل", "House", "Residential", "house", 4);
        await EnsureAsync(context, "studio", "استوديو", "Studio", "Residential", "studio", 5);

        await EnsureAsync(context, "residential-land", "أرض سكنية", "Residential Land", "Land", "land-residential", 10);
        await EnsureAsync(context, "agricultural-land", "أرض زراعية", "Agricultural Land", "Land", "land-agricultural", 11);
        await EnsureAsync(context, "industrial-land", "أرض صناعية", "Industrial Land", "Land", "land-industrial", 12);
        await EnsureAsync(context, "commercial-land", "أرض تجارية", "Commercial Land", "Land", "land-commercial", 13);

        await EnsureAsync(context, "shop", "محل تجاري", "Shop", "Commercial", "shop", 20);
        await EnsureAsync(context, "office", "مكتب", "Office", "Commercial", "office", 21);
        await EnsureAsync(context, "warehouse", "مستودع", "Warehouse", "Commercial", "warehouse", 22);
        await EnsureAsync(context, "hotel", "فندق", "Hotel", "Commercial", "hotel", 23);

        await context.SaveChangesAsync();
    }

    private static async Task EnsureAsync(
        AppDbContext context,
        string code,
        string nameAr,
        string nameEn,
        string category,
        string? icon,
        int sortOrder)
    {
        var normalizedCode = code.Trim().ToLowerInvariant();

        var exists = await context.PropertyTypes.AnyAsync(pt =>
            pt.Code == normalizedCode);

        if (exists)
            return;

        context.PropertyTypes.Add(
            PropertyType.Create(
                code: normalizedCode,
                nameAr: nameAr,
                nameEn: nameEn,
                category: category,
                icon: icon,
                sortOrder: sortOrder));
    }
}