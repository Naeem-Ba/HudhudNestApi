using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HudhudNestApi.Domain.Lookups.Entities;
using Microsoft.EntityFrameworkCore;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// يُشغَّل تلقائياً عند تشغيل المشروع أول مرة.
/// كيف يعمل: يتحقق من وجود السجلات أولاً (idempotent) —
/// لن يُضيف بيانات مكررة إذا شُغِّل مرة ثانية.
/// </summary>
public static class CurrencySeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        // إذا كانت البيانات موجودة بالفعل، أوقف
        if (await context.Currencies.AnyAsync())
            return;

        var currencies = new List<Currency>
        {
            Currency.Create(
                code: "USD",
                nameEn: "US Dollar",
                nameAr: "الدولار الأمريكي",
                symbol: "$",
                exchangeRateToUSD: 1m,      // العملة الأساسية
                isBaseCurrency: true,
                decimalPlaces: 2),

            Currency.Create(
                code: "SYP",
                nameEn: "Syrian Pound",
                nameAr: "الليرة السورية",
                symbol: "ل.س",
                exchangeRateToUSD: 14000m,  // تقريباً — يُحدَّث يومياً
                decimalPlaces: 0),           // الليرة السورية بدون كسور

            Currency.Create(
                code: "EUR",
                nameEn: "Euro",
                nameAr: "اليورو",
                symbol: "€",
                exchangeRateToUSD: 0.92m,
                decimalPlaces: 2),

            Currency.Create(
                code: "TRY",
                nameEn: "Turkish Lira",
                nameAr: "الليرة التركية",
                symbol: "₺",
                exchangeRateToUSD: 32.5m,
                decimalPlaces: 2),

            Currency.Create(
                code: "AED",
                nameEn: "UAE Dirham",
                nameAr: "الدرهم الإماراتي",
                symbol: "د.إ",
                exchangeRateToUSD: 3.67m,
                decimalPlaces: 2),
        };

        await context.Currencies.AddRangeAsync(currencies);
        await context.SaveChangesAsync();
    }

}
