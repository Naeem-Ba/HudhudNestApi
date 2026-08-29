using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace PropertyApi.Domain.Lookups.Entities;

/// <summary>
/// جدول العملات المدعومة.
/// لماذا؟ السوق السوري يتعامل بـ SYP/USD/EUR/TRY/AED.
/// ExchangeRateToUSD يُحدَّث يومياً بـ BackgroundService.
/// لا نخزن BasePriceInUSD في Property لأن سعر الصرف متغير.
/// </summary>
public class Currency
{
    public int Id { get; private set; }

    /// <summary>ISO 4217 code (e.g. "SYP", "USD", "EUR")</summary>
    public string Code { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>e.g. "ل.س", "$", "€"</summary>
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>
    /// كم وحدة من هذه العملة = 1 دولار.
    /// مثال: SYP/USD = 14000 (أي 14000 ليرة = 1 دولار)
    /// </summary>
    public decimal ExchangeRateToUSD { get; private set; }

    public DateTime ExchangeRateUpdatedAt { get; private set; }

    /// <summary>هل هي العملة الأساسية للمقارنة؟ (USD هو الأساس)</summary>
    public bool IsBaseCurrency { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>عدد الخانات العشرية للعرض (SYP=0, USD=2, EUR=2)</summary>
    public int DecimalPlaces { get; private set; } = 2;

    // EF Core constructor
    private Currency() { }

    public static Currency Create(
        string code, string nameEn, string nameAr,
        string symbol, decimal exchangeRateToUSD,
        bool isBaseCurrency = false, int decimalPlaces = 2)
    {
        return new Currency
        {
            Code = code.ToUpperInvariant(),
            NameEn = nameEn,
            NameAr = nameAr,
            Symbol = symbol,
            ExchangeRateToUSD = exchangeRateToUSD,
            ExchangeRateUpdatedAt = DateTime.UtcNow,
            IsBaseCurrency = isBaseCurrency,
            DecimalPlaces = decimalPlaces
        };
    }

    public void UpdateExchangeRate(decimal newRate)
    {
        ExchangeRateToUSD = newRate;
        ExchangeRateUpdatedAt = DateTime.UtcNow;
    }
}