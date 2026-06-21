using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Enums;
using PropertyApi.Domain.Lookups.Entities;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Domain.Users.Entities;


namespace PropertyApi.Domain.Listings.Entities;

/// <summary>
/// Core listing entity. Represents a real estate property for rent or sale.
/// DDD: private setters + factory method + domain methods enforce business rules.
/// </summary>
public class Property : AuditableEntity
{
    // -- Core Info ----------------------------------------------
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    // -- Address (flat — no FK to Cities table for now) ---------
    // For global support: store free-text city + ISO country code.
    // If you later add a Cities catalog, add a nullable CityId FK here.
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }          // State / Governorate / Province
    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "DE", "SY", "US").</summary>
    public string CountryCode { get; set; } = "DE";
    public string? PostalCode { get; set; }

    // -- Geo Coordinates ----------------------------------------
    // decimal instead of double: avoids IEEE 754 floating-point errors
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // -- Listing Type -------------------------------------------
    // Replaces two nullable booleans (ZumMieten / ZumKaufen)
    // Enum prevents the invalid state: ForRent=false & ForSale=false
    public ListingType ListingType { get; set; }

    // -- Pricing ------------------------------------------------
    public decimal? ColdRent { get; set; }   // Monthly cold rent
    public decimal? WarmRent { get; set; }   // Monthly warm rent (incl. utilities)
    public decimal? PurchasePrice { get; set; }   // Sale price
    public decimal? AdditionalCosts { get; set; }   // Monthly additional costs (Nebenkosten)
    public decimal? Deposit { get; set; }   // Security deposit (Kaution)

    /// <summary>ISO 4217 currency code (e.g. "EUR", "USD", "SYP").</summary>
    public string CurrencyCode { get; set; } = "SYP";

    // -- Property Details ---------------------------------------
    public int? Rooms { get; set; }
    /// <summary>Area in m². Use decimal — not double — for precision.</summary>
    public decimal? Area { get; set; }
    public int? Floor { get; set; }
    public int? TotalFloors { get; set; }
    public DateTime? AvailableFrom { get; set; }

    // -- Features -----------------------------------------------
    public bool HasBalcony { get; set; }
    public bool HasElevator { get; set; }
    public bool HasParkingSpace { get; set; }
    public HeatingType HeatingType { get; set; } = HeatingType.Unknown;

    // -- Status / Classification --------------------------------
    public PropertyStatus Status { get; set; } = PropertyStatus.Available;
    public PropertyCondition Condition { get; set; } = PropertyCondition.Unknown;
    public EnergyEfficiencyType EnergyEfficiency { get; set; } = EnergyEfficiencyType.NotAvailable;

    // -- Ownership ----------------------------------------------
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    // -- Publishing ---------------------------------------------
    public bool IsPublished { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public DateTime? ExpiresAt { get; set; }


    // ── الموقع الجغرافي المنظَّم ──────────────────────────────────────

    /// <summary>
    /// المحافظة (FK → Governorates).
    /// nullable للتوافق مع البيانات القديمة.
    /// الحقل City الموجود يبقى للتوافق مع السجلات السابقة.
    /// </summary>
    public int? GovernorateId { get; set; }
    public int? DistrictId { get; set; }
    public int? NeighborhoodId { get; set; }

    /// <summary>
    /// أقرب علامة مميزة — جوهري في سوريا.
    /// "بجانب جامع السلطان" أوضح من رقم الشارع.
    /// </summary>
    public string? NearestLandmark { get; set; }

    public string? BuildingNumber { get; set; }
    public string? GoogleMapsUrl { get; set; }

    // ── نوع العقار ──────────────────────────────────────────────────────

    /// <summary>FK → PropertyTypes (شقة/فيلا/محل/أرض)</summary>
    public int? PropertyTypeId { get; set; }

    /// <summary>
    /// الوسيط المسؤول عن الإعلان (إن وجد).
    /// FK → Users (نفس جدول المستخدمين، دور Agent)
    /// </summary>
    public Guid? AgentId { get; set; }

    // ── الوضع القانوني (Critical للسوق السوري) ──────────────────────

    public LegalStatusType LegalStatus { get; set; } = LegalStatusType.Unknown;
    public string? LegalStatusNotes { get; set; }
    public ZoningStatusType ZoningStatus { get; set; } = ZoningStatusType.Unknown;
    public string? ZoningNotes { get; set; }

    /// <summary>هل يوجد نزاع قانوني على العقار؟</summary>
    public bool HasLegalDispute { get; set; }
    public string? LegalDisputeNotes { get; set; }

    // ── مواصفات إضافية ──────────────────────────────────────────────────

    public int? LivingRoomsCount { get; set; }
    public int? KitchenCount { get; set; }
    public int? ParkingCount { get; set; }
    public int? YearBuilt { get; set; }
    public FurnishingStatus FurnishingStatus { get; set; } = FurnishingStatus.Unfurnished;

    // ── الخدمات والمرافق (خاص بالسوق السوري) ─────────────────────────

    public bool HasElectricity { get; set; }

    /// <summary>ساعات الكهرباء يومياً (0-24) — شائع في سوريا</summary>
    public byte? ElectricityHoursPerDay { get; set; }

    public bool HasGenerator { get; set; }
    public bool HasSolarPanels { get; set; }
    public bool HasWater { get; set; }

    /// <summary>PublicNetwork / Well / Tank / Mixed</summary>
    public string? WaterSource { get; set; }

    /// <summary>أيام وصول المياه أسبوعياً (1-7)</summary>
    public byte? WaterDaysPerWeek { get; set; }

    public bool HasGas { get; set; }
    public bool HasInternet { get; set; }

    /// <summary>ADSL / Fiber / 4G / None</summary>
    public string? InternetType { get; set; }

    public bool HasAC { get; set; }
    public bool HasView { get; set; }
    public string? ViewDescription { get; set; }

    // ── التسعير بالعملة ────────────────────────────────────────────────

    /// <summary>
    /// FK → Currencies — العملة الأصلية للسعر المُعلَن.
    /// لا نخزّن BasePriceInUSD هنا — نحسبه عند الاستعلام.
    /// السبب: سعر الصرف يتغير يومياً، والقيمة المخزونة تصبح خاطئة.
    /// </summary>
    public int? PriceCurrencyId { get; set; }

    // ── الميتاداتا ─────────────────────────────────────────────────────

    public int ViewsCount { get; set; }
    public int FavoritesCount { get; set; }

    /// <summary>تم التحقق من العقار بالمعاينة الفعلية؟</summary>
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }

    /// <summary>إعلان مميز (مدفوع)</summary>
    public bool IsFeatured { get; set; }
    public DateTime? FeaturedUntil { get; set; }

    // ── Navigation الجديدة ────────────────────────────────────────────

    public Governorate? Governorate { get; set; }
    public District? District { get; set; }
    public Neighborhood? Neighborhood { get; set; }
    public PropertyType? PropertyType { get; set; }
    public Currency? PriceCurrency { get; set; }
    public User? Agent { get; set; }
    public SaleDetails? SaleDetails { get; set; }
    public RentalDetails? RentalDetails { get; set; }

    // -- Navigation ---------------------------------------------
    public ICollection<PropertyImage> Images { get; set; } = new List<PropertyImage>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<PropertyAmenity> PropertyAmenities { get; set; } = new List<PropertyAmenity>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    // -- DDD: Private constructor (EF Core needs it too) --------
    private Property() { }

    // -- DDD: Factory method — the only way to create a valid Property
    public static Property Create(
        string title,
        string description,
        Guid ownerId,
        ListingType listingType,
        string countryCode = "SY",
        string currencyCode = "SYP")
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Property title is required.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Property description is required.");
        if (ownerId == Guid.Empty)
            throw new DomainException("OwnerId is required.");

        return new Property
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Description = description.Trim(),
            OwnerId = ownerId,
            ListingType = listingType,
            CountryCode = countryCode.ToUpperInvariant(),
            CurrencyCode = currencyCode.ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // -- Domain Methods -----------------------------------------
    public void UpdateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Property title cannot be empty.");
        Title = title.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description cannot be empty.");
        Description = description.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Publish()
    {
        if (IsPublished)
            throw new DomainException("Property is already published.");
        IsPublished = true;
        PublishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unpublish()
    {
        IsPublished = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsDeleted(Guid deletedByUserId)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedByUserId = deletedByUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeStatus(PropertyStatus newStatus)
    {
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }
}
