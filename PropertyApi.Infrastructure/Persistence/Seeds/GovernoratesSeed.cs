using Microsoft.EntityFrameworkCore;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// Seeds Syria's 14 governorates and their official districts (مناطق).
///
/// Data source / verification: the 14-governorate list and the district list
/// per governorate were verified via Wikipedia's "Governorates of Syria" and
/// "Districts of Syria" articles (checked for any post-Dec-2024 administrative
/// reorganization; none found as of this pass — see docs/phase-0-implementation.md
/// section 9 for the full research trail). This is the *official* administrative
/// tier — do not confuse it with informal city quarters/neighborhoods, which
/// live in <see cref="NeighborhoodsSeed"/> instead (a pre-existing bug in this
/// file used to seed Damascus's city quarters — Mazzeh, Kafr Sousa, etc. — as
/// "Districts" here, and even mis-attributed Jaramana to Damascus governorate
/// when it actually belongs to Rif Dimashq. Fixed as part of this pass.)
/// </summary>
public static class GovernoratesSeed
{
    /// <summary>
    /// Exposes each governorate's generated district IDs by district name-en,
    /// so <see cref="NeighborhoodsSeed"/> can attach neighborhoods without
    /// re-querying. Populated by <see cref="SeedAsync"/>.
    /// </summary>
    public static readonly Dictionary<string, int> DistrictIdsByNameEn = new();

    public static async Task SeedAsync(AppDbContext context)
    {
        var damascus = await EnsureGovernorateAsync(context, "دمشق", "Damascus", "SY", 1);
        var rifDimashq = await EnsureGovernorateAsync(context, "ريف دمشق", "Rural Damascus", "SY", 2);
        var aleppo = await EnsureGovernorateAsync(context, "حلب", "Aleppo", "SY", 3);
        var homs = await EnsureGovernorateAsync(context, "حمص", "Homs", "SY", 4);
        var hama = await EnsureGovernorateAsync(context, "حماة", "Hama", "SY", 5);
        var latakia = await EnsureGovernorateAsync(context, "اللاذقية", "Latakia", "SY", 6);
        var tartus = await EnsureGovernorateAsync(context, "طرطوس", "Tartus", "SY", 7);
        var hasakah = await EnsureGovernorateAsync(context, "الحسكة", "Al-Hasakah", "SY", 8);
        var deirEzZor = await EnsureGovernorateAsync(context, "دير الزور", "Deir ez-Zor", "SY", 9);
        var raqqa = await EnsureGovernorateAsync(context, "الرقة", "Raqqa", "SY", 10);
        var idlib = await EnsureGovernorateAsync(context, "إدلب", "Idlib", "SY", 11);
        var daraa = await EnsureGovernorateAsync(context, "درعا", "Daraa", "SY", 12);
        var quneitra = await EnsureGovernorateAsync(context, "القنيطرة", "Quneitra", "SY", 13);
        var suwayda = await EnsureGovernorateAsync(context, "السويداء", "As-Suwayda", "SY", 14);

        // Damascus governorate = the city itself; officially a single district.
        // City-quarter granularity lives under NeighborhoodsSeed, attached to
        // this one district.
        await EnsureDistrictAsync(context, damascus, "مدينة دمشق", "Damascus City", 1);

        // إضافة تالية (بعد "مدينة دمشق" أعلاه): التقسيم الإداري الرسمي الفعلي
        // لمدينة دمشق — ١٦ منطقة (مصدر: ويكيبيديا، تحقّق المستخدم منه مباشرة).
        // أُضيفت جنب "مدينة دمشق" القديمة دون حذفها أو حذف أحيائها العشرين
        // الحالية (قرار المستخدم صراحة، لتفادي كسر أي بيانات عقارات فعلية
        // مرتبطة بها) — يبقى تعارض أسماء متوقَّع بين بعض هذه المناطق وبعض
        // "أحياء" القائمة القديمة (مثلاً "كفر سوسة"، "المزة"، "برزة" ظهرت
        // هناك كأحياء وهنا كمناطق)، وهذا موثَّق وواعٍ، لا خطأ برمجي.
        await EnsureDistrictAsync(context, damascus, "دمشق القديمة", "Old Damascus", 2);
        await EnsureDistrictAsync(context, damascus, "ساروجة", "Sarouja", 3);
        await EnsureDistrictAsync(context, damascus, "القنوات", "Al-Qanawat", 4);
        await EnsureDistrictAsync(context, damascus, "جوبر", "Jobar", 5);
        await EnsureDistrictAsync(context, damascus, "الميدان", "Al-Midan", 6);
        await EnsureDistrictAsync(context, damascus, "الشاغور", "Al-Shaghour", 7);
        await EnsureDistrictAsync(context, damascus, "القدم", "Al-Qadam", 8);
        await EnsureDistrictAsync(context, damascus, "كفر سوسة", "Kafr Sousa", 9);
        await EnsureDistrictAsync(context, damascus, "المزة", "Mazzeh", 10);
        await EnsureDistrictAsync(context, damascus, "دمر", "Dummar", 11);
        await EnsureDistrictAsync(context, damascus, "برزة", "Barza", 12);
        await EnsureDistrictAsync(context, damascus, "القابون", "Al-Qaboun", 13);
        await EnsureDistrictAsync(context, damascus, "ركن الدين", "Rukn al-Din", 14);
        await EnsureDistrictAsync(context, damascus, "الصالحية", "Al-Salihiyah", 15);
        await EnsureDistrictAsync(context, damascus, "المهاجرين", "Al-Muhajireen", 16);
        await EnsureDistrictAsync(context, damascus, "اليرموك", "Yarmouk", 17);

        await EnsureDistrictAsync(context, rifDimashq, "مركز ريف دمشق", "Markaz Rif Dimashq", 1);
        await EnsureDistrictAsync(context, rifDimashq, "داريا", "Darayya", 2);
        await EnsureDistrictAsync(context, rifDimashq, "دوما", "Douma", 3);
        await EnsureDistrictAsync(context, rifDimashq, "النبك", "An-Nabek", 4);
        await EnsureDistrictAsync(context, rifDimashq, "قطنا", "Qatana", 5);
        await EnsureDistrictAsync(context, rifDimashq, "قدسيا", "Qudsaya", 6);
        await EnsureDistrictAsync(context, rifDimashq, "القطيفة", "Al-Qutayfah", 7);
        await EnsureDistrictAsync(context, rifDimashq, "التل", "Al-Tall", 8);
        await EnsureDistrictAsync(context, rifDimashq, "يبرود", "Yabroud", 9);
        await EnsureDistrictAsync(context, rifDimashq, "الزبداني", "Al-Zabadani", 10);

        await EnsureDistrictAsync(context, aleppo, "جبل سمعان", "Mount Simeon (Jabal Saman)", 1);
        await EnsureDistrictAsync(context, aleppo, "عفرين", "Afrin", 2);
        await EnsureDistrictAsync(context, aleppo, "الأتارب", "Atarib", 3);
        await EnsureDistrictAsync(context, aleppo, "عين العرب", "Ayn al-Arab (Kobani)", 4);
        await EnsureDistrictAsync(context, aleppo, "اعزاز", "Azaz", 5);
        await EnsureDistrictAsync(context, aleppo, "الباب", "Al-Bab", 6);
        await EnsureDistrictAsync(context, aleppo, "دير حافر", "Dayr Hafir", 7);
        await EnsureDistrictAsync(context, aleppo, "جرابلس", "Jarabulus", 8);
        await EnsureDistrictAsync(context, aleppo, "منبج", "Manbij", 9);
        await EnsureDistrictAsync(context, aleppo, "السفيرة", "Safirah", 10);

        await EnsureDistrictAsync(context, homs, "حمص", "Homs", 1);
        await EnsureDistrictAsync(context, homs, "المخرم", "Al-Mukharram", 2);
        await EnsureDistrictAsync(context, homs, "القصير", "Al-Qusayr", 3);
        await EnsureDistrictAsync(context, homs, "الرستن", "Ar-Rastan", 4);
        await EnsureDistrictAsync(context, homs, "تدمر", "Tadmur (Palmyra)", 5);
        await EnsureDistrictAsync(context, homs, "تلدو", "Taldou", 6);
        await EnsureDistrictAsync(context, homs, "تلكلخ", "Talkalakh", 7);

        await EnsureDistrictAsync(context, hama, "حماة", "Hama", 1);
        await EnsureDistrictAsync(context, hama, "مصياف", "Masyaf", 2);
        await EnsureDistrictAsync(context, hama, "محردة", "Mahardah", 3);
        await EnsureDistrictAsync(context, hama, "سلمية", "Salamiyah", 4);
        await EnsureDistrictAsync(context, hama, "السقيلبية", "Al-Suqaylabiyah", 5);

        await EnsureDistrictAsync(context, latakia, "اللاذقية", "Latakia", 1);
        await EnsureDistrictAsync(context, latakia, "الحفة", "Al-Haffah", 2);
        await EnsureDistrictAsync(context, latakia, "جبلة", "Jableh", 3);
        await EnsureDistrictAsync(context, latakia, "القرداحة", "Qardaha", 4);

        await EnsureDistrictAsync(context, tartus, "طرطوس", "Tartus", 1);
        await EnsureDistrictAsync(context, tartus, "بانياس", "Baniyas", 2);
        await EnsureDistrictAsync(context, tartus, "دريكيش", "Duraykish", 3);
        await EnsureDistrictAsync(context, tartus, "صافيتا", "Safita", 4);
        await EnsureDistrictAsync(context, tartus, "الشيخ بدر", "Al-Shaykh Badr", 5);

        await EnsureDistrictAsync(context, hasakah, "الحسكة", "Al-Hasakah", 1);
        await EnsureDistrictAsync(context, hasakah, "المالكية", "Al-Malikiyah", 2);
        await EnsureDistrictAsync(context, hasakah, "القامشلي", "Qamishli", 3);
        await EnsureDistrictAsync(context, hasakah, "رأس العين", "Ra's al-Ayn", 4);
        await EnsureDistrictAsync(context, hasakah, "الشدادة", "Al-Shaddadah", 5);

        await EnsureDistrictAsync(context, deirEzZor, "دير الزور", "Deir ez-Zor", 1);
        await EnsureDistrictAsync(context, deirEzZor, "البوكمال", "Abu Kamal", 2);
        await EnsureDistrictAsync(context, deirEzZor, "الميادين", "Mayadin", 3);

        await EnsureDistrictAsync(context, raqqa, "الرقة", "Raqqa", 1);
        await EnsureDistrictAsync(context, raqqa, "تل أبيض", "Tell Abyad", 2);
        await EnsureDistrictAsync(context, raqqa, "الطبقة", "Tabqa", 3);

        await EnsureDistrictAsync(context, idlib, "إدلب", "Idlib", 1);
        await EnsureDistrictAsync(context, idlib, "أريحا", "Arihah", 2);
        await EnsureDistrictAsync(context, idlib, "حارم", "Harem", 3);
        await EnsureDistrictAsync(context, idlib, "جسر الشغور", "Jisr al-Shughur", 4);
        await EnsureDistrictAsync(context, idlib, "معرة النعمان", "Ma'arrat al-Numan", 5);

        await EnsureDistrictAsync(context, daraa, "درعا", "Daraa", 1);
        await EnsureDistrictAsync(context, daraa, "إزرع", "Izra", 2);
        await EnsureDistrictAsync(context, daraa, "الصنمين", "Al-Sanamayn", 3);

        await EnsureDistrictAsync(context, quneitra, "القنيطرة", "Quneitra", 1);
        await EnsureDistrictAsync(context, quneitra, "فيق", "Fiq", 2);

        await EnsureDistrictAsync(context, suwayda, "السويداء", "As-Suwayda", 1);
        await EnsureDistrictAsync(context, suwayda, "صلخد", "Salkhad", 2);
        await EnsureDistrictAsync(context, suwayda, "شهبا", "Shahba", 3);

        await context.SaveChangesAsync();
    }

    private static async Task<int> EnsureGovernorateAsync(
        AppDbContext context,
        string nameAr,
        string nameEn,
        string countryCode,
        int sortOrder)
    {
        var existingId = await context.Governorates
            .Where(g => g.NameAr == nameAr && g.CountryCode == countryCode)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync();

        if (existingId.HasValue)
            return existingId.Value;

        var governorate = Governorate.Create(
            nameAr: nameAr,
            nameEn: nameEn,
            countryCode: countryCode,
            sortOrder: sortOrder);

        context.Governorates.Add(governorate);

        await context.SaveChangesAsync();

        return governorate.Id;
    }

    /// <summary>
    /// Ensures a district exists and records its ID under <paramref name="nameEn"/>
    /// in <see cref="DistrictIdsByNameEn"/> so NeighborhoodsSeed can find it —
    /// including on a re-run where the row already existed (previously this
    /// method returned void, so a caller had no way to get the ID back at all).
    /// </summary>
    private static async Task EnsureDistrictAsync(
        AppDbContext context,
        int governorateId,
        string nameAr,
        string nameEn,
        int sortOrder)
    {
        var existingId = await context.Districts
            .Where(d => d.GovernorateId == governorateId && d.NameAr == nameAr)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync();

        if (existingId.HasValue)
        {
            DistrictIdsByNameEn[nameEn] = existingId.Value;
            return;
        }

        var district = District.Create(
            governorateId: governorateId,
            nameAr: nameAr,
            nameEn: nameEn,
            sortOrder: sortOrder);

        context.Districts.Add(district);

        await context.SaveChangesAsync();

        DistrictIdsByNameEn[nameEn] = district.Id;
    }
}
