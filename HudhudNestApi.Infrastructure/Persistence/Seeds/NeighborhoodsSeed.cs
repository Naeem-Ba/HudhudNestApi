using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// Seeds named city neighborhoods (أحياء) — the finest location tier.
///
/// IMPORTANT — deliberately incomplete, and that is by design, not an
/// oversight: unlike governorates/districts, Syria has no single official,
/// exhaustive neighborhood registry. After a research pass (see
/// docs/phase-0-implementation.md section 9), reasonably well-documented
/// neighborhood lists could only be verified for three cities: Damascus,
/// Homs, and Latakia. Every other district's neighborhood list is
/// intentionally left empty rather than filled with guessed/unverified
/// names, which would silently mislead users searching by a neighborhood
/// that doesn't correspond to a real place.
///
/// This is exactly why Property.NeighborhoodId is optional and
/// Property.NeighborhoodText exists as a free-text fallback — see the
/// frontend's property-form: users in a district with no seeded
/// neighborhoods can still name their neighborhood manually.
///
/// Extending coverage later: run EnsureDistrictAsync in GovernoratesSeed for
/// the target district first (if not already there), then add an
/// EnsureNeighborhoodAsync block here keyed off GovernoratesSeed.DistrictIdsByNameEn.
/// </summary>
public static class NeighborhoodsSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        var districtIds = GovernoratesSeed.DistrictIdsByNameEn;

        if (districtIds.TryGetValue("Damascus City", out var damascusCityId))
        {
            await EnsureNeighborhoodAsync(context, damascusCityId, "المزة", "Mazzeh", 1);
            await EnsureNeighborhoodAsync(context, damascusCityId, "كفر سوسة", "Kafr Sousa", 2);
            await EnsureNeighborhoodAsync(context, damascusCityId, "المزرعة", "Al-Mazra'a", 3);
            await EnsureNeighborhoodAsync(context, damascusCityId, "الشعلان", "Al-Sha'lan", 4);
            await EnsureNeighborhoodAsync(context, damascusCityId, "أبو رمانة", "Abu Rumaneh", 5);
            await EnsureNeighborhoodAsync(context, damascusCityId, "المالكي", "Al-Malki", 6);
            await EnsureNeighborhoodAsync(context, damascusCityId, "القدم", "Al-Qadam", 7);
            await EnsureNeighborhoodAsync(context, damascusCityId, "اليرموك", "Yarmouk", 8);
            await EnsureNeighborhoodAsync(context, damascusCityId, "برزة", "Barza", 9);
            await EnsureNeighborhoodAsync(context, damascusCityId, "دمر", "Dummar", 10);
            await EnsureNeighborhoodAsync(context, damascusCityId, "جوبر", "Jobar", 11);
            await EnsureNeighborhoodAsync(context, damascusCityId, "الميدان", "Al-Midan", 12);
            await EnsureNeighborhoodAsync(context, damascusCityId, "الشاغور", "Al-Shaghour", 13);
            await EnsureNeighborhoodAsync(context, damascusCityId, "القنوات", "Al-Qanawat", 14);
            await EnsureNeighborhoodAsync(context, damascusCityId, "القيمرية", "Al-Qaymariyya", 15);
            await EnsureNeighborhoodAsync(context, damascusCityId, "العمارة", "Al-Amara", 16);
            await EnsureNeighborhoodAsync(context, damascusCityId, "الحريقة", "Al-Hariqa", 17);
            await EnsureNeighborhoodAsync(context, damascusCityId, "ساروجة", "Sarouja", 18);
            await EnsureNeighborhoodAsync(context, damascusCityId, "باب توما", "Bab Touma", 19);
            await EnsureNeighborhoodAsync(context, damascusCityId, "باب شرقي", "Bab Sharqi", 20);
        }

        if (districtIds.TryGetValue("Homs", out var homsId))
        {
            await EnsureNeighborhoodAsync(context, homsId, "الحميدية", "Al-Hamidiyah", 1);
            await EnsureNeighborhoodAsync(context, homsId, "الخالدية", "Al-Khalidiyah", 2);
            await EnsureNeighborhoodAsync(context, homsId, "البياضة", "Al-Bayada", 3);
            await EnsureNeighborhoodAsync(context, homsId, "دير بعلبة", "Deir Baalbah", 4);
            await EnsureNeighborhoodAsync(context, homsId, "باب هود", "Bab Hud", 5);
            await EnsureNeighborhoodAsync(context, homsId, "باب تدمر", "Bab Tadmur", 6);
            await EnsureNeighborhoodAsync(context, homsId, "باب الدريب", "Bab al-Dreib", 7);
            await EnsureNeighborhoodAsync(context, homsId, "باب السباع", "Bab al-Sebaa", 8);
            await EnsureNeighborhoodAsync(context, homsId, "الوعر", "Al-Waer", 9);
            await EnsureNeighborhoodAsync(context, homsId, "بابا عمرو", "Baba Amr", 10);
            await EnsureNeighborhoodAsync(context, homsId, "كرم الزيتون", "Karm al-Zaytoun", 11);
            await EnsureNeighborhoodAsync(context, homsId, "الزهراء", "Al-Zahra", 12);
            await EnsureNeighborhoodAsync(context, homsId, "الإنشاءات", "Al-Inshaat", 13);
            await EnsureNeighborhoodAsync(context, homsId, "الغوطة", "Al-Ghouta", 14);
            await EnsureNeighborhoodAsync(context, homsId, "القصور", "Al-Qusour", 15);
        }

        if (districtIds.TryGetValue("Latakia", out var latakiaId))
        {
            await EnsureNeighborhoodAsync(context, latakiaId, "الصليبة", "Al-Salibah", 1);
            await EnsureNeighborhoodAsync(context, latakiaId, "الشيخ ضاهر", "Sheikh Dhaher", 2);
            await EnsureNeighborhoodAsync(context, latakiaId, "الفاروس", "Al-Farous", 3);
            await EnsureNeighborhoodAsync(context, latakiaId, "العوينة", "Al-Awina", 4);
            await EnsureNeighborhoodAsync(context, latakiaId, "القلعة", "Al-Qalaa", 5);
            await EnsureNeighborhoodAsync(context, latakiaId, "الطابيات", "Al-Tabiyat", 6);
            await EnsureNeighborhoodAsync(context, latakiaId, "الزراعة", "Al-Ziraa", 7);
            await EnsureNeighborhoodAsync(context, latakiaId, "الدعتور", "Al-Da'tour", 8);
            await EnsureNeighborhoodAsync(context, latakiaId, "الرمل الفلسطيني", "Raml Filastini", 9);
            await EnsureNeighborhoodAsync(context, latakiaId, "المشروع السابع", "Al-Mashrou Al-Sabi", 10);
            await EnsureNeighborhoodAsync(context, latakiaId, "دمسرخو", "Damsarkho", 11);
            await EnsureNeighborhoodAsync(context, latakiaId, "السكنتوري", "Al-Sakantouri", 12);
        }

        // Jaramana: a well-known Rif Dimashq town, worth having even though
        // it's not in the three fully-seeded cities above — kept as a single
        // neighborhood entry under its real district (Markaz Rif Dimashq),
        // fixing the pre-existing bug where it was wrongly filed directly
        // under Damascus governorate as a "district".
        if (districtIds.TryGetValue("Markaz Rif Dimashq", out var markazRifDimashqId))
        {
            await EnsureNeighborhoodAsync(context, markazRifDimashqId, "جرمانا", "Jaramana", 1);
        }

        // ==========================================================
        // أحياء مناطق دمشق الـ١٦ الرسمية (أُضيفت في GovernoratesSeed أعلاه).
        // مصدر: طلب المستخدم (بيانات ويكيبيديا) + تحقّق تكميلي من marefa.org.
        //
        // ملاحظة أمانة بيانات مهمة: من أصل الـ١٦ منطقة، ثبَت لي فعليًا (تطابق
        // تام مع العدد الذي أرسله المستخدم) ثلاث مناطق فقط: دمشق القديمة،
        // القنوات، وبرزة. باقي المناطق (ساروجة، جوبر، الميدان، الشاغور، القدم،
        // كفر سوسة، المزة، دمر، القابون، ركن الدين، الصالحية، المهاجرين،
        // اليرموك) تُركت بدون أحياء عمدًا — المصادر المتاحة لي أعطت إما لا شيء
        // أو قوائم لا تطابق العدد الذي ذكره المستخدم (مثال: الميدان أرسل ٦ ووجدت
        // ٣ فقط؛ الشاغور أرسل ٩ ووجدت ٢ فقط)، ونفس فلسفة هذا الملف نفسها
        // (التعليق أعلى الملف) تقول: عدم تخمين أفضل من بيانات غير موثوقة.
        // إذا زوّدني المستخدم بالقائمة الكاملة الموثوقة لهذه المناطق لاحقًا،
        // تُضاف بنفس النمط أدناه.
        // ==========================================================

        if (districtIds.TryGetValue("Old Damascus", out var oldDamascusId))
        {
            await EnsureNeighborhoodAsync(context, oldDamascusId, "الجورة", "Al-Jura", 1);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "العمارة الجوانية", "Al-Amara Al-Jawaniyah", 2);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "باب توما", "Bab Touma", 3);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "القيمرية", "Al-Qaymariyya", 4);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "الحميدية", "Al-Hamidiyah", 5);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "الحريقة", "Al-Hariqa", 6);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "الأمين", "Al-Amin", 7);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "مئذنة الشحم", "Ma'thanat Al-Shahm", 8);
            await EnsureNeighborhoodAsync(context, oldDamascusId, "شاغور جواني", "Shaghour Jawani", 9);
        }

        if (districtIds.TryGetValue("Al-Qanawat", out var qanawatId))
        {
            await EnsureNeighborhoodAsync(context, qanawatId, "قنوات تعديل", "Qanawat Tadeel", 1);
            await EnsureNeighborhoodAsync(context, qanawatId, "بركة", "Barakah", 2);
            await EnsureNeighborhoodAsync(context, qanawatId, "زقاق الحطاب", "Zuqaq Al-Hattab", 3);
            await EnsureNeighborhoodAsync(context, qanawatId, "حيوطية", "Hayyutiyah", 4);
            await EnsureNeighborhoodAsync(context, qanawatId, "شاذبكية", "Shathbakiyah", 5);
            await EnsureNeighborhoodAsync(context, qanawatId, "براني", "Barrani", 6);
            await EnsureNeighborhoodAsync(context, qanawatId, "جواني", "Jawani", 7);
            await EnsureNeighborhoodAsync(context, qanawatId, "باب سريجه", "Bab Sreijeh", 8);
            await EnsureNeighborhoodAsync(context, qanawatId, "قبر عاتكة", "Qabr Atikah", 9);
        }

        if (districtIds.TryGetValue("Barza", out var barzaId))
        {
            await EnsureNeighborhoodAsync(context, barzaId, "العباس", "Al-Abbas", 1);
            await EnsureNeighborhoodAsync(context, barzaId, "برزة البلد", "Barza Al-Balad", 2);
            await EnsureNeighborhoodAsync(context, barzaId, "عش الورور", "Ish Al-Warwar", 3);
            await EnsureNeighborhoodAsync(context, barzaId, "المنارة", "Al-Manara", 4);
            await EnsureNeighborhoodAsync(context, barzaId, "مساكن برزة", "Masaken Barza", 5);
            await EnsureNeighborhoodAsync(context, barzaId, "النزهة", "Al-Nuzha", 6);
        }

        await context.SaveChangesAsync();
    }

    private static async Task EnsureNeighborhoodAsync(
        AppDbContext context,
        int districtId,
        string nameAr,
        string nameEn,
        int sortOrder)
    {
        var exists = await context.Neighborhoods.AnyAsync(n =>
            n.DistrictId == districtId && n.NameAr == nameAr);

        if (exists)
            return;

        context.Neighborhoods.Add(
            Neighborhood.Create(
                districtId: districtId,
                nameAr: nameAr,
                nameEn: nameEn,
                sortOrder: sortOrder));
    }
}
