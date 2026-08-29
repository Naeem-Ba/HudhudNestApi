using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Enums;
using PropertyApi.Domain.ShortStay.ValueObjects;

namespace PropertyApi.Domain.ShortStay.Entities;

/// <summary>
/// Short-stay accommodation listing — a standalone aggregate, only optionally linked to a
/// traditional Property (PropertyId is nullable: most short-stay hosts will never have a
/// Property record at all). DDD: private setters + Create() factory + grouped update
/// methods, same shape as Property/VisitRequest.
/// </summary>
public sealed class ShortStayListing : AuditableEntity
{
    // ── References ────────────────────────────────────────────────
    public Guid OwnerId { get; private set; }
    public Guid? PropertyId { get; private set; }
    public int AccommodationTypeId { get; private set; }

    // ── Basic info ────────────────────────────────────────────────
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public int Bedrooms { get; private set; }
    public int Bathrooms { get; private set; }
    public TimeOnly CheckInTime { get; private set; }
    public TimeOnly CheckOutTime { get; private set; }
    public bool SelfCheckInEnabled { get; private set; }

    // ── Booking settings ──────────────────────────────────────────
    public bool InstantBookingEnabled { get; private set; }
    public bool RequestBookingEnabled { get; private set; } = true;

    // ── Location ──────────────────────────────────────────────────
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public int? GovernorateId { get; private set; }
    public int? DistrictId { get; private set; }
    public int? NeighborhoodId { get; private set; }
    public string? City { get; private set; }
    public LocationVisibility LocationVisibility { get; private set; } = LocationVisibility.Approximate;

    // ── Amenity extras ────────────────────────────────────────────
    public PoolDetails? PoolDetails { get; private set; }

    // ── Fees (listing-level defaults) ──────────────────────────────
    public decimal CleaningFee { get; private set; }
    public decimal ExtraGuestFee { get; private set; }
    public decimal ExtraBedFee { get; private set; }

    // ── House rules ───────────────────────────────────────────────
    public bool AllowsSmoking { get; private set; }
    public bool AllowsParties { get; private set; }
    public bool AllowsPets { get; private set; }
    public TimeOnly? QuietHoursStart { get; private set; }
    public TimeOnly? QuietHoursEnd { get; private set; }
    public string? CustomRulesText { get; private set; }

    // ── Cancellation policy (template — snapshotted onto each Booking at creation) ──
    public int CancellationFreeCancellationDays { get; private set; } = 1;
    public bool CancellationDepositRefundable { get; private set; }
    public string? CancellationCustomTermsText { get; private set; }

    /// <summary>Percentage (0-100) of the total stay price required as a deposit at booking
    /// time. Null means no deposit is required — the guest pays in full per PaymentMethod.</summary>
    public decimal? DepositPercentage { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────
    public bool IsPublished { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    public AccommodationType AccommodationType { get; private set; } = null!;
    public ICollection<RoomType> RoomTypes { get; private set; } = new List<RoomType>();
    public ICollection<ShortStayListingAmenity> ListingAmenities { get; private set; } = new List<ShortStayListingAmenity>();
    public ICollection<ShortStayListingPhoto> Photos { get; private set; } = new List<ShortStayListingPhoto>();

    private ShortStayListing() { }

    public static ShortStayListing Create(
        Guid ownerId,
        int accommodationTypeId,
        string title,
        string description,
        int capacity,
        int bedrooms,
        int bathrooms,
        TimeOnly checkInTime,
        TimeOnly checkOutTime,
        decimal latitude,
        decimal longitude,
        Guid? propertyId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان الإعلان مطلوب.");

        if (capacity < 1)
            throw new DomainException("سعة الإعلان يجب أن تكون ضيفاً واحداً على الأقل.");

        return new ShortStayListing
        {
            OwnerId = ownerId,
            PropertyId = propertyId,
            AccommodationTypeId = accommodationTypeId,
            Title = title.Trim(),
            Description = description.Trim(),
            Capacity = capacity,
            Bedrooms = bedrooms,
            Bathrooms = bathrooms,
            CheckInTime = checkInTime,
            CheckOutTime = checkOutTime,
            Latitude = latitude,
            Longitude = longitude,
        };
    }

    // ── Domain update methods ─────────────────────────────────────
    public void UpdateBasicInfo(string title, string description, int capacity, int bedrooms, int bathrooms,
        TimeOnly checkInTime, TimeOnly checkOutTime, bool selfCheckInEnabled)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان الإعلان مطلوب.");
        if (capacity < 1)
            throw new DomainException("سعة الإعلان يجب أن تكون ضيفاً واحداً على الأقل.");

        Title = title.Trim();
        Description = description.Trim();
        Capacity = capacity;
        Bedrooms = bedrooms;
        Bathrooms = bathrooms;
        CheckInTime = checkInTime;
        CheckOutTime = checkOutTime;
        SelfCheckInEnabled = selfCheckInEnabled;
    }

    public void UpdateBookingSettings(bool instantBookingEnabled, bool requestBookingEnabled)
    {
        if (!instantBookingEnabled && !requestBookingEnabled)
            throw new DomainException("يجب تفعيل وضع حجز واحد على الأقل (فوري أو بالطلب).");

        InstantBookingEnabled = instantBookingEnabled;
        RequestBookingEnabled = requestBookingEnabled;
    }

    public void UpdateLocation(decimal latitude, decimal longitude, int? governorateId, int? districtId,
        int? neighborhoodId, string? city, LocationVisibility locationVisibility)
    {
        Latitude = latitude;
        Longitude = longitude;
        GovernorateId = governorateId;
        DistrictId = districtId;
        NeighborhoodId = neighborhoodId;
        City = city;
        LocationVisibility = locationVisibility;
    }

    public void UpdatePoolDetails(PoolDetails? poolDetails) => PoolDetails = poolDetails;

    public void UpdateFees(decimal cleaningFee, decimal extraGuestFee, decimal extraBedFee)
    {
        if (cleaningFee < 0 || extraGuestFee < 0 || extraBedFee < 0)
            throw new DomainException("قيم الرسوم لا يمكن أن تكون سالبة.");

        CleaningFee = cleaningFee;
        ExtraGuestFee = extraGuestFee;
        ExtraBedFee = extraBedFee;
    }

    public void UpdateHouseRules(bool allowsSmoking, bool allowsParties, bool allowsPets,
        TimeOnly? quietHoursStart, TimeOnly? quietHoursEnd, string? customRulesText)
    {
        AllowsSmoking = allowsSmoking;
        AllowsParties = allowsParties;
        AllowsPets = allowsPets;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        CustomRulesText = customRulesText?.Trim();
    }

    public void UpdateCancellationPolicy(int freeCancellationDays, bool depositRefundable, string? customTermsText)
    {
        if (freeCancellationDays < 0)
            throw new DomainException("عدد أيام الإلغاء المجاني لا يمكن أن يكون سالباً.");

        CancellationFreeCancellationDays = freeCancellationDays;
        CancellationDepositRefundable = depositRefundable;
        CancellationCustomTermsText = customTermsText?.Trim();
    }

    public void UpdateDepositPolicy(decimal? depositPercentage)
    {
        if (depositPercentage is < 0 or > 100)
            throw new DomainException("نسبة العربون يجب أن تكون بين 0 و 100.");

        DepositPercentage = depositPercentage;
    }

    /// <summary>
    /// Replaces the full amenity set in one call (not additive) — mirrors how the host-facing
    /// form actually works (a checklist submitted as a whole), and avoids ever accumulating
    /// duplicate rows for the same AmenityId. Does not validate that each id refers to a real,
    /// active Amenity — the FK constraint on ShortStayListingAmenities.AmenityId is the backstop
    /// (same trust level CreatePropertyCommandHandler already applies to PropertyAmenities).
    /// </summary>
    public void SetAmenities(IReadOnlyCollection<Guid> amenityIds)
    {
        ListingAmenities.Clear();
        foreach (var amenityId in amenityIds.Distinct())
        {
            ListingAmenities.Add(new ShortStayListingAmenity { ShortStayListingId = Id, AmenityId = amenityId });
        }
    }

    /// <summary>Builds the immutable snapshot text stored on a Booking at creation time.</summary>
    public string BuildHouseRulesSnapshotText()
    {
        var parts = new List<string>
        {
            AllowsSmoking ? "التدخين مسموح" : "التدخين ممنوع",
            AllowsParties ? "الحفلات مسموحة" : "الحفلات ممنوعة",
            AllowsPets ? "الحيوانات الأليفة مسموحة" : "الحيوانات الأليفة ممنوعة",
        };

        if (QuietHoursStart.HasValue && QuietHoursEnd.HasValue)
            parts.Add($"ساعات الهدوء: {QuietHoursStart.Value:HH\\:mm} - {QuietHoursEnd.Value:HH\\:mm}");

        if (!string.IsNullOrWhiteSpace(CustomRulesText))
            parts.Add(CustomRulesText);

        return string.Join(" | ", parts);
    }

    public void Publish()
    {
        if (RoomTypes.Count == 0)
            throw new DomainException("لا يمكن نشر إعلان بلا أي نوع غرفة/وحدة قابلة للحجز.");

        IsPublished = true;
        PublishedAt = DateTime.UtcNow;
    }

    public void Unpublish() => IsPublished = false;

    /// <summary>Soft delete — mirrors Property.MarkAsDeleted exactly. The repository's
    /// Remove() call afterward is intercepted by AppDbContext.SaveChangesAsync and converted
    /// to a plain update (IsDeleted/DeletedAt only); DeletedByUserId is not touched by that
    /// generic interceptor, so it must be set here.</summary>
    public void MarkAsDeleted(Guid deletedByUserId)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedByUserId = deletedByUserId;
        UpdatedAt = DateTime.UtcNow;
    }
}
