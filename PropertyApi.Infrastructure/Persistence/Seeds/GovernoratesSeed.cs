using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Lookups.Entities;
using Microsoft.EntityFrameworkCore;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

public static class GovernoratesSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.Governorates.AnyAsync())
            return;

        // 14 محافظة سورية رسمية
        var governorates = new List<Governorate>
        {
            Governorate.Create("دمشق",       "Damascus",     sortOrder: 1),
            Governorate.Create("ريف دمشق",   "Rural Damascus", sortOrder: 2),
            Governorate.Create("حلب",        "Aleppo",       sortOrder: 3),
            Governorate.Create("حمص",        "Homs",         sortOrder: 4),
            Governorate.Create("حماة",       "Hama",         sortOrder: 5),
            Governorate.Create("اللاذقية",   "Latakia",      sortOrder: 6),
            Governorate.Create("طرطوس",      "Tartus",       sortOrder: 7),
            Governorate.Create("الحسكة",     "Al-Hasakah",   sortOrder: 8),
            Governorate.Create("دير الزور",  "Deir ez-Zor",  sortOrder: 9),
            Governorate.Create("الرقة",      "Raqqa",        sortOrder: 10),
            Governorate.Create("إدلب",       "Idlib",        sortOrder: 11),
            Governorate.Create("درعا",       "Daraa",        sortOrder: 12),
            Governorate.Create("القنيطرة",   "Quneitra",     sortOrder: 13),
            Governorate.Create("السويداء",   "As-Suwayda",   sortOrder: 14),
        };

        await context.Governorates.AddRangeAsync(governorates);
        await context.SaveChangesAsync();

        // مثال على بعض المناطق في دمشق (يمكن توسيعه)
        var damascusId = await context.Governorates
            .Where(g => g.NameAr == "دمشق")
            .Select(g => g.Id)
            .FirstAsync();

        var districts = new List<District>
        {
            District.Create(damascusId, "مزة",      "Mazzeh",    sortOrder: 1),
            District.Create(damascusId, "كفر سوسة",  "Kafr Sousa", sortOrder: 2),
            District.Create(damascusId, "المزرعة",   "Al-Mazra'a", sortOrder: 3),
            District.Create(damascusId, "الشعلان",   "Al-Sha'lan", sortOrder: 4),
            District.Create(damascusId, "أبو رمانة", "Abu Rumaneh", sortOrder: 5),
            District.Create(damascusId, "المالكي",   "Al-Malki",   sortOrder: 6),
            District.Create(damascusId, "جرمانا",    "Jaramana",   sortOrder: 7),
            District.Create(damascusId, "القدم",     "Al-Qadam",   sortOrder: 8),
        };

        await context.Districts.AddRangeAsync(districts);
        await context.SaveChangesAsync();
    }
}