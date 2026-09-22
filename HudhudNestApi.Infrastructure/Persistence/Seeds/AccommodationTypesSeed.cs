using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

/// <summary>Seeds the 16 initial short-stay accommodation categories (plan §4), grouped by
/// Category (PrivateResidence/Tourism/Hospitality/Other). Same idempotent Ensure-by-code
/// pattern as PropertyTypesSeed — new types can be added later via the Admin panel without
/// a code change or redeploy.</summary>
public static class AccommodationTypesSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        // Private Residence
        await EnsureAsync(context, "furnished-apartment", "شقة مفروشة", "Furnished Apartment", "PrivateResidence", "apartment", 1);
        await EnsureAsync(context, "house", "منزل", "House", "PrivateResidence", "house", 2);
        await EnsureAsync(context, "villa", "فيلا", "Villa", "PrivateResidence", "villa", 3);
        await EnsureAsync(context, "country-house", "منزل ريفي", "Country House", "PrivateResidence", "country-house", 4);
        await EnsureAsync(context, "farm", "مزرعة", "Farm", "PrivateResidence", "farm", 5);

        // Tourism
        await EnsureAsync(context, "chalet", "شاليه", "Chalet", "Tourism", "chalet", 10);
        await EnsureAsync(context, "cabin", "كوخ", "Cabin", "Tourism", "cabin", 11);
        await EnsureAsync(context, "resort", "منتجع", "Resort", "Tourism", "resort", 12);
        await EnsureAsync(context, "rest-house", "بيت استراحة", "Rest House", "Tourism", "rest-house", 13);

        // Hospitality
        await EnsureAsync(context, "hotel", "فندق", "Hotel", "Hospitality", "hotel", 20);
        await EnsureAsync(context, "hotel-room", "غرفة فندقية", "Hotel Room", "Hospitality", "hotel-room", 21);
        await EnsureAsync(context, "aparthotel", "شقق فندقية", "Aparthotel", "Hospitality", "aparthotel", 22);
        await EnsureAsync(context, "guest-house", "بيت ضيافة", "Guest House", "Hospitality", "guest-house", 23);

        // Other
        await EnsureAsync(context, "camp", "مخيم", "Camp", "Other", "camp", 30);
        await EnsureAsync(context, "caravan", "كرفان", "Caravan", "Other", "caravan", 31);
        await EnsureAsync(context, "rural-stay", "إقامة ريفية", "Rural Stay", "Other", "rural-stay", 32);

        await context.SaveChangesAsync();
    }

    private static async Task EnsureAsync(
        AppDbContext context, string code, string nameAr, string nameEn, string category, string? icon, int sortOrder)
    {
        var normalizedCode = code.Trim().ToLowerInvariant();

        if (await context.AccommodationTypes.AnyAsync(t => t.Code == normalizedCode))
            return;

        context.AccommodationTypes.Add(AccommodationType.Create(normalizedCode, nameAr, nameEn, category, icon, sortOrder));
    }
}
