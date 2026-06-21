using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Domain.Listings.Entities;

/// <summary>
/// تفاصيل البيع — One-to-One مع Property.
/// لماذا جدول منفصل؟
/// حقول البيع مختلفة جذرياً عن حقول الإيجار.
/// دمجهم في جدول واحد يُنتج عشرات الحقول الـ NULL لكل سجل،
/// مما يصعّب الكود ويُعقّد الـ Validation.
///
/// ملاحظة: لا يوجد حقل MinAcceptablePrice هنا.
/// سبب الحذف: تخزين السعر السري في DB خطر أمني مباشر.
/// البديل: تشفير تطبيقي (IDataProtector) إذا احتجت لاحقاً.
/// </summary>
public class SaleDetails
{
    public int Id { get; private set; }

    /// <summary>ربط One-to-One مع Property</summary>
    public Guid PropertyId { get; private set; }

    // -- التسعير -----------------------------------------------
    public decimal TotalPrice { get; private set; }
    public int PriceCurrencyId { get; private set; }

    public bool IsPriceNegotiable { get; private set; } = true;

    // -- طريقة الدفع -------------------------------------------
    /// <summary>Cash / Installments / Both</summary>
    public string PaymentMethod { get; private set; } = "Cash";

    public int? InstallmentsYears { get; private set; }

    /// <summary>نسبة الدفعة الأولى %</summary>
    public decimal? DownPaymentPercentage { get; private set; }

    public decimal? DownPaymentAmount { get; private set; }
    public decimal? MonthlyInstallment { get; private set; }
    public string? InstallmentNotes { get; private set; }

    /// <summary>رسوم نقل الملكية %</summary>
    public decimal? TransferFeePercentage { get; private set; }

    // -- ما يشمله البيع ----------------------------------------
    public bool IncludesFurniture { get; private set; }
    public bool IncludesAppliances { get; private set; }
    public string? ExtraInclusions { get; private set; }

    // Navigation
    public Property? Property { get; private set; }
    public Currency? PriceCurrency { get; private set; }

    private SaleDetails() { }

    public static SaleDetails Create(
        Guid propertyId,
        decimal totalPrice,
        int priceCurrencyId,
        bool isPriceNegotiable = true,
        string paymentMethod = "Cash")
    {
        if (totalPrice <= 0)
            throw new DomainException("السعر الإجمالي يجب أن يكون أكبر من صفر.");

        return new SaleDetails
        {
            PropertyId = propertyId,
            TotalPrice = totalPrice,
            PriceCurrencyId = priceCurrencyId,
            IsPriceNegotiable = isPriceNegotiable,
            PaymentMethod = paymentMethod
        };
    }

    public void SetInstallmentDetails(
        int years, decimal downPaymentPct,
        decimal monthlyInstallment, string? notes = null)
    {
        if (years <= 0)
            throw new DomainException("عدد سنوات التقسيط يجب أن يكون موجباً.");
        if (downPaymentPct is < 0 or > 100)
            throw new DomainException("نسبة الدفعة الأولى يجب أن تكون بين 0 و100.");

        InstallmentsYears = years;
        DownPaymentPercentage = downPaymentPct;
        DownPaymentAmount = TotalPrice * (downPaymentPct / 100);
        MonthlyInstallment = monthlyInstallment;
        InstallmentNotes = notes;
    }

    public void SetInclusions(bool furniture, bool appliances, string? extras = null)
    {
        IncludesFurniture = furniture;
        IncludesAppliances = appliances;
        ExtraInclusions = extras?.Trim();
    }
}
