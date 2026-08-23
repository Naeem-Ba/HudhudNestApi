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
    public string CountryCode { get; set; } = "SY";
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
    public UserAccount? Owner { get; set; }

    /// <summary>
    /// المكتب العقاري الذي نُشر الإعلان باسمه — null للمالك المستقل، وهو حال كل
    /// الإعلانات القائمة.
    ///
    /// هذا حقل إضافي بجانب OwnerId لا بديل عنه: الملكية والمسؤولية تبقيان للمستخدم،
    /// والمكتب مجرد نسبة تُعرض ويمكن التصفية بها. لا يوجد فلتر عام عليه، فلا استعلام
    /// قائم يتأثر، ومغادرة المستخدم لمكتبه لا تحذف إعلاناته ولا تُخفيها.
    /// </summary>
    public Guid? AgencyId { get; private set; }

    // -- Publishing ---------------------------------------------
    public bool IsPublished { get; private set; } = true;
    public DateTime? PublishedAt { get; private set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// When the owner was last warned that this listing is about to expire. Null means
    /// no warning is outstanding for the current publication window.
    ///
    /// This exists purely for idempotency: ListingExpiryHostedService sweeps on a timer,
    /// so without a stamp every sweep inside the warning window would raise another
    /// notification. Cleared by ExtendPublication() so the next window warns again.
    /// </summary>
    public DateTime? ExpiryWarningSentAt { get; private set; }

    /// <summary>
    /// Last time the owner explicitly confirmed this listing is still available.
    /// Phase-0 "freshness" feature: the frontend shows a staleness banner (and the
    /// search/matching logic can eventually deprioritize) listings that have gone
    /// too long (30 days) without confirmation. Null means never confirmed —
    /// treated the same as stale.
    /// </summary>
    public DateTime? LastConfirmedAvailableAt { get; private set; }


    // ── الموقع الجغرافي المنظَّم ──────────────────────────────────────

    /// <summary>
    /// المحافظة (FK → Governorates).
    /// nullable للتوافق مع البيانات القديمة.
    /// الحقل City الموجود يبقى للتوافق مع السجلات السابقة.
    /// </summary>
    public int? GovernorateId { get; set; }
    public int? DistrictId { get; set; }

    /// <summary>
    /// Manual fallback name when the listing's district isn't in the seeded
    /// Districts table (a small locality not in the ~65-district seed — see
    /// GovernoratesSeed.cs). Mutually exclusive in intent with DistrictId
    /// (the frontend clears one when the other is set). Setting this
    /// auto-submits a LocationSuggestion for admin review — see
    /// LocationSuggestionService.
    /// </summary>
    public string? DistrictText { get; set; }

    public int? NeighborhoodId { get; set; }

    /// <summary>
    /// Manual fallback name when the listing's neighborhood isn't in the
    /// seeded Neighborhoods table (true for most of Syria today — see
    /// NeighborhoodsSeed.cs for which districts are actually covered).
    /// Mutually exclusive in intent with NeighborhoodId (the frontend clears
    /// one when the other is set) but nothing at this layer enforces that;
    /// treat NeighborhoodId as authoritative when both are present. Setting
    /// this auto-submits a LocationSuggestion for admin review — see
    /// LocationSuggestionService.
    /// </summary>
    public string? NeighborhoodText { get; set; }

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

    /// <summary>
    /// إعلان مميز (مدفوع) — يُرفع في ترتيب نتائج البحث طوال المدة المدفوعة.
    ///
    /// كان هذان الحقلان معطَّلين تماماً: لا كود يقرؤهما ولا يكتبهما، فهرس وحيد فقط.
    /// صارا الآن محكومَين بـ MarkFeatured/ClearFeatured وبمسار دفع، ولذلك أُغلقت
    /// الواضعات (setters): تمييز إعلان صار نتيجة معاملة مالية مكتملة، لا إسناداً
    /// يستطيع أي مُعالِج تنفيذه.
    /// </summary>
    public bool IsFeatured { get; private set; }
    public DateTime? FeaturedUntil { get; private set; }

    // ── Navigation الجديدة ────────────────────────────────────────────

    public Governorate? Governorate { get; set; }
    public District? District { get; set; }
    public Neighborhood? Neighborhood { get; set; }
    public PropertyType? PropertyType { get; set; }
    public Currency? PriceCurrency { get; set; }
    public UserAccount? Agent { get; set; }
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
        string currencyCode = "SYP",
        bool isPublished = true)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Property title is required.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Property description is required.");
        if (ownerId == Guid.Empty)
            throw new DomainException("OwnerId is required.");

        var now = DateTime.UtcNow;

        return new Property
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Description = description.Trim(),
            OwnerId = ownerId,
            ListingType = listingType,
            CountryCode = countryCode.ToUpperInvariant(),
            CurrencyCode = currencyCode.ToUpperInvariant(),
            IsPublished = isPublished,
            PublishedAt = isPublished ? now : null,
            CreatedAt = now,
            UpdatedAt = now
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

    /// <summary>
    /// Owner explicitly confirms the listing is still available. Cheap, single-tap
    /// action from the frontend — resets the staleness clock without requiring a
    /// full edit. See LastConfirmedAvailableAt.
    /// </summary>
    public void ConfirmStillAvailable()
    {
        LastConfirmedAvailableAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    // -- Listing lifecycle (publication window) ------------------
    //
    // Search already hides a listing whose ExpiresAt has passed (PropertyRepository
    // filters on it), so these methods are not what makes an expired listing invisible.
    // What they add is the part that was missing: a durable status the owner can see and
    // act on, a one-shot warning, and a grace clock that ends in deletion.

    /// <summary>
    /// Records that the owner has been warned about the approaching expiry, so the
    /// scheduler does not warn again for this publication window.
    /// </summary>
    public void MarkExpiryWarningSent()
    {
        ExpiryWarningSentAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Transitions a listing whose publication window has elapsed into Expired and takes
    /// it off the public site. Called only by ListingExpiryHostedService.
    /// </summary>
    /// <exception cref="DomainException">
    /// If ExpiresAt is null. A listing with no expiry date has no window to have elapsed,
    /// and silently expiring it would delete a listing that was never on a clock.
    /// </exception>
    public void MarkExpired()
    {
        if (ExpiresAt is null)
            throw new DomainException("Cannot expire a listing that has no ExpiresAt.");

        Status = PropertyStatus.Expired;
        IsPublished = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Grants another publication window — the effect of a paid single-listing extension.
    /// Restores the listing to Available and republishes it.
    /// </summary>
    /// <remarks>
    /// The new window is measured from <paramref name="fromUtc"/> rather than from the old
    /// ExpiresAt: an owner who pays two weeks into the grace period gets a full period from
    /// the day they paid, not a period already partly spent.
    /// </remarks>
    public void ExtendPublication(TimeSpan period, DateTime fromUtc)
    {
        if (period <= TimeSpan.Zero)
            throw new DomainException("Extension period must be positive.");

        ExpiresAt = fromUtc.Add(period);
        ExpiryWarningSentAt = null;
        Status = PropertyStatus.Available;
        IsPublished = true;
        PublishedAt ??= fromUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Deletes an expired listing once its grace period has run out.
    /// </summary>
    /// <remarks>
    /// Soft delete, deliberately. This sets the same IsDeleted flag MarkAsDeleted() uses,
    /// which the global query filter on Property already honours, so the listing vanishes
    /// from every read path while the row survives for audit and recovery. Nothing in this
    /// codebase physically removes a Property — even repository Remove() is intercepted and
    /// converted to IsDeleted=true — and an automated sweep is the last place to break that.
    ///
    /// DeletedByUserId is left null because no user deleted this; the scheduler did.
    /// </remarks>
    public void MarkExpiredListingDeletedBySystem()
    {
        if (Status != PropertyStatus.Expired)
            throw new DomainException("Only an expired listing can be deleted by the expiry sweep.");

        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    // -- Agency attribution --------------------------------------

    /// <summary>
    /// Attributes the listing to an agency, or clears the attribution when passed null.
    /// </summary>
    /// <remarks>
    /// Attribution only. The listing's owner, its visibility and every permission check
    /// are unchanged — nothing in this codebase grants access on the strength of a shared
    /// AgencyId, because a Property still belongs to its OwnerId.
    /// </remarks>
    public void SetAgency(Guid? agencyId)
    {
        AgencyId = agencyId == Guid.Empty ? null : agencyId;
        UpdatedAt = DateTime.UtcNow;
    }

    // -- Featured placement (paid) -------------------------------

    /// <summary>
    /// Promotes the listing for a paid period, measured from <paramref name="fromUtc"/>.
    /// </summary>
    /// <remarks>
    /// Called only after a FeaturedListingFee transaction has been marked Completed. Like
    /// ExtendPublication, the window runs from the moment of payment rather than from any
    /// previous FeaturedUntil, so a late payer gets the full period they bought.
    ///
    /// Buying again while still featured EXTENDS from the later of "now" and the current
    /// FeaturedUntil, so a second purchase adds to the remaining time instead of throwing
    /// it away. Anything else would quietly charge someone for days they already owned.
    /// </remarks>
    /// <exception cref="DomainException">
    /// If the listing is expired or deleted. Promoting a listing that is not visible would
    /// take money for a placement nobody can see.
    /// </exception>
    public void MarkFeatured(TimeSpan period, DateTime fromUtc)
    {
        if (period <= TimeSpan.Zero)
            throw new DomainException("Featured period must be positive.");

        if (IsDeleted)
            throw new DomainException("لا يمكن تمييز إعلان محذوف.");

        if (Status == PropertyStatus.Expired)
            throw new DomainException("لا يمكن تمييز إعلان منتهٍ — مدّد الإعلان أولاً.");

        var startsFrom = FeaturedUntil is { } until && until > fromUtc
            ? until
            : fromUtc;

        IsFeatured = true;
        FeaturedUntil = startsFrom.Add(period);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Ends the featured placement. Called by the scheduler once FeaturedUntil has passed,
    /// and by an admin reversing a placement.
    /// </summary>
    /// <remarks>
    /// FeaturedUntil is kept rather than nulled: it is the record of what was paid for and
    /// when it ran out. IsFeatured alone decides whether the listing is promoted today.
    /// </remarks>
    public void ClearFeatured()
    {
        IsFeatured = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Whether the listing should be promoted at <paramref name="asOfUtc"/>. Guards against
    /// the state where IsFeatured is still true but the paid window has already elapsed —
    /// the sweep clears that within its interval, and read paths must not wait for it.
    /// </summary>
    public bool IsCurrentlyFeatured(DateTime asOfUtc)
        => IsFeatured && FeaturedUntil is { } until && until > asOfUtc;
}
