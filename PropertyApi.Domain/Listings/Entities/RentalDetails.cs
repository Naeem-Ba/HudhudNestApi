using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Domain.Listings.Entities;

/// <summary>
/// تفاصيل الإيجار — One-to-One مع Property.
/// يحتوي على حقول خاصة بالإيجار لا علاقة لها بالبيع.
/// </summary>
public class RentalDetails
{
    public int Id { get; private set; }
    public Guid PropertyId { get; private set; }

    // -- الإيجار الشهري ---------------------------------------
    public decimal MonthlyRent { get; private set; }
    public int RentCurrencyId { get; private set; }

    // -- شروط العقد -------------------------------------------
    /// <summary>الحد الأدنى لمدة العقد بالأشهر (default: 12)</summary>
    public int MinContractMonths { get; private set; } = 12;
    public int? MaxContractMonths { get; private set; }

    /// <summary>التأمين بعدد الأشهر (default: 2)</summary>
    public int SecurityDepositMonths { get; private set; } = 2;
    public decimal? SecurityDepositAmount { get; private set; }

    /// <summary>Monthly / Quarterly / BiAnnual / Annual</summary>
    public string PaymentFrequency { get; private set; } = "Monthly";

    // -- قواعد السكن ------------------------------------------
    /// <summary>Family / Singles / Bachelor / Any</summary>
    public string? AllowedTenantType { get; private set; }
    public bool PetsAllowed { get; private set; }
    public bool SmokingAllowed { get; private set; }
    public bool CommercialUseAllowed { get; private set; }

    // -- الإتاحة -----------------------------------------------
    public DateOnly? AvailableFrom { get; private set; }

    // -- الفواتير الشاملة --------------------------------------
    public bool IncludesWaterBill { get; private set; }
    public bool IncludesElectricityBill { get; private set; }
    public bool IncludesInternetBill { get; private set; }

    public string? RenewalPolicy { get; private set; }

    // Navigation
    public Property? Property { get; private set; }
    public Currency? RentCurrency { get; private set; }

    private RentalDetails() { }

    public static RentalDetails Create(
        Guid propertyId,
        decimal monthlyRent,
        int rentCurrencyId,
        int minContractMonths = 12,
        int securityDepositMonths = 2)
    {
        if (monthlyRent <= 0)
            throw new DomainException("قيمة الإيجار يجب أن تكون أكبر من صفر.");
        if (minContractMonths < 1)
            throw new DomainException("مدة العقد الدنيا يجب أن تكون شهراً واحداً على الأقل.");

        return new RentalDetails
        {
            PropertyId = propertyId,
            MonthlyRent = monthlyRent,
            RentCurrencyId = rentCurrencyId,
            MinContractMonths = minContractMonths,
            SecurityDepositMonths = securityDepositMonths
        };
    }

    public void SetTenantRules(
        string? allowedTenantType, bool petsAllowed,
        bool smokingAllowed, bool commercialUseAllowed)
    {
        AllowedTenantType = allowedTenantType;
        PetsAllowed = petsAllowed;
        SmokingAllowed = smokingAllowed;
        CommercialUseAllowed = commercialUseAllowed;
    }

    public void SetIncludedBills(bool water, bool electricity, bool internet)
    {
        IncludesWaterBill = water;
        IncludesElectricityBill = electricity;
        IncludesInternetBill = internet;
    }
}
