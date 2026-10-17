using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// Seeds the reference list of property amenities (the "المرافق" checkboxes of the listing forms, the
/// search filter and the detail page). No code path created these rows before, so a fresh database
/// answered <c>GET /api/lookups/amenities</c> with an empty list and the apps showed "no amenities".
///
/// Name is the Arabic label (the primary market language and what older clients render as-is);
/// IconName is a stable language-neutral key the app translates (AMENITIES_CATALOG.* in its i18n
/// files). Idempotent: a row is added only when neither its key nor its Arabic name exists yet
/// (soft-deleted rows included, the unique Name index covers them too), so admin-made rows and
/// rows already in Production are never touched.
/// </summary>
public static class AmenitiesSeed
{
    public static IReadOnlyList<(string Key, string NameAr, string Category)> Catalog { get; } = new[]
    {
        ("wifi", "إنترنت / واي فاي", "Comfort"),
        ("air-conditioning", "تكييف", "Comfort"),
        ("heating", "تدفئة", "Comfort"),
        ("solar-heater", "سخان شمسي", "Comfort"),
        ("solar-energy", "طاقة شمسية", "Comfort"),
        ("generator", "مولدة كهرباء", "Comfort"),
        ("ups-batteries", "بطاريات / UPS", "Comfort"),
        ("water-tank", "خزان مياه", "Comfort"),
        ("central-heating", "تدفئة مركزية", "Comfort"),
        ("elevator", "مصعد", "Building"),
        ("parking", "موقف سيارات", "Building"),
        ("garage", "كراج", "Building"),
        ("garden", "حديقة", "Outdoor"),
        ("balcony", "شرفة / بلكون", "Outdoor"),
        ("terrace", "تراس", "Outdoor"),
        ("roof-access", "سطح خاص", "Outdoor"),
        ("pool", "مسبح", "Outdoor"),
        ("bbq-area", "منطقة شواء", "Outdoor"),
        ("sea-view", "إطلالة بحرية", "View"),
        ("mountain-view", "إطلالة جبلية", "View"),
        ("security-guard", "حارس / أمن", "Security"),
        ("cctv", "كاميرات مراقبة", "Security"),
        ("intercom", "إنتركوم", "Security"),
        ("fire-safety", "نظام إطفاء", "Security"),
        ("furnished-kitchen", "مطبخ مجهّز", "Kitchen"),
        ("washing-machine", "غسالة", "Appliances"),
        ("refrigerator", "براد", "Appliances"),
        ("oven-stove", "فرن وغاز", "Appliances"),
        ("tv", "تلفاز", "Appliances"),
        ("gym", "نادي رياضي", "Facilities"),
        ("playground", "ملعب أطفال", "Facilities"),
        ("storage-room", "غرفة تخزين", "Facilities"),
        ("pets-allowed", "يُسمح بالحيوانات الأليفة", "Rules"),
        ("wheelchair-access", "مدخل لذوي الاحتياجات الخاصة", "Facilities"),
    };

    public static async Task SeedAsync(AppDbContext context)
    {
        var existing = await context.Amenities
            .IgnoreQueryFilters()
            .Select(a => new { a.Name, a.IconName })
            .ToListAsync();

        var names = existing.Select(a => a.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = existing
            .Where(a => !string.IsNullOrWhiteSpace(a.IconName))
            .Select(a => a.IconName!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, nameAr, category) in Catalog)
        {
            if (keys.Contains(key) || names.Contains(nameAr))
                continue;

            context.Amenities.Add(new Amenity { Name = nameAr, Category = category, IconName = key });
        }

        await context.SaveChangesAsync();
    }
}
