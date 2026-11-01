using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Demand.Enums;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Domain.Demand.Entities;

/// <summary>
/// Growth engine P2: a buyer's or renter's "I am looking for…" post. Unlike a saved search (private,
/// any filter) it is a demand signal advertisers can see, anonymously, on the request board, and it ends:
/// it expires, can be paused, resumed and closed. The requester's identity never leaves the API through
/// any request endpoint (DECISIONS.md D-04).
/// </summary>
public sealed class PropertySearchRequest : BaseEntity
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(60);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(90);
    public const int DetailsMaxLength = 500;

    public Guid UserId { get; private set; }

    /// <summary>ForSale (buy) or ForRent (rent). ForRentAndSale is not a request type.</summary>
    public ListingType ListingType { get; private set; }

    public int GovernorateId { get; private set; }
    public int? DistrictId { get; private set; }
    public int? NeighborhoodId { get; private set; }
    public int? PropertyTypeId { get; private set; }

    public decimal? MinBudget { get; private set; }
    public decimal MaxBudget { get; private set; }

    /// <summary>One of the search currencies (USD, EUR, SYP).</summary>
    public string CurrencyCode { get; private set; } = "USD";

    public int? MinRooms { get; private set; }
    public decimal? MinArea { get; private set; }
    public decimal? MaxArea { get; private set; }

    public bool NeedsBalcony { get; private set; }
    public bool NeedsElevator { get; private set; }
    public bool NeedsParking { get; private set; }
    public bool NeedsFurnished { get; private set; }

    /// <summary>Free text shown to advertisers; the validator refuses contact details in it.</summary>
    public string? Details { get; private set; }

    public SearchRequestStatus Status { get; private set; } = SearchRequestStatus.Active;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    /// <summary>
    /// Listings changed (published or edited) up to this moment have been evaluated. Null means the
    /// first sweep has not run yet: it evaluates the current market once without notifying per listing.
    /// </summary>
    public DateTime? MatchedUpTo { get; private set; }

    private PropertySearchRequest() { }

    public static PropertySearchRequest Create(
        Guid userId,
        ListingType listingType,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        int? propertyTypeId,
        decimal? minBudget,
        decimal maxBudget,
        string currencyCode,
        int? minRooms,
        decimal? minArea,
        decimal? maxArea,
        bool needsBalcony,
        bool needsElevator,
        bool needsParking,
        bool needsFurnished,
        string? details,
        DateTime? expiresAt,
        DateTime nowUtc)
    {
        if (userId == Guid.Empty)
            throw new DomainException("المستخدم مطلوب.");

        var request = new PropertySearchRequest { UserId = userId, CreatedAt = nowUtc, UpdatedAt = nowUtc };
        request.Apply(listingType, governorateId, districtId, neighborhoodId, propertyTypeId, minBudget, maxBudget,
            currencyCode, minRooms, minArea, maxArea, needsBalcony, needsElevator, needsParking, needsFurnished, details);
        request.ExpiresAt = ClampExpiry(expiresAt, nowUtc);
        return request;
    }

    public void Update(
        ListingType listingType,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        int? propertyTypeId,
        decimal? minBudget,
        decimal maxBudget,
        string currencyCode,
        int? minRooms,
        decimal? minArea,
        decimal? maxArea,
        bool needsBalcony,
        bool needsElevator,
        bool needsParking,
        bool needsFurnished,
        string? details,
        DateTime? expiresAt,
        DateTime nowUtc)
    {
        EnsureOpen();
        Apply(listingType, governorateId, districtId, neighborhoodId, propertyTypeId, minBudget, maxBudget,
            currencyCode, minRooms, minArea, maxArea, needsBalcony, needsElevator, needsParking, needsFurnished, details);
        if (expiresAt.HasValue)
            ExpiresAt = ClampExpiry(expiresAt, nowUtc);

        // New criteria: the current market is evaluated again on the next sweep (matches already
        // stored and announced stay, the unique key keeps them from being announced twice).
        MatchedUpTo = null;
        UpdatedAt = nowUtc;
    }

    public void Pause(DateTime nowUtc)
    {
        EnsureOpen();
        if (Status != SearchRequestStatus.Active)
            throw new InvalidStateTransitionException("الطلب غير نشط.");
        Status = SearchRequestStatus.Paused;
        UpdatedAt = nowUtc;
    }

    public void Resume(DateTime nowUtc)
    {
        EnsureOpen();
        if (Status != SearchRequestStatus.Paused)
            throw new InvalidStateTransitionException("الطلب ليس موقوفًا.");
        if (ExpiresAt <= nowUtc)
            throw new InvalidStateTransitionException("انتهت مدة الطلب. عدّل تاريخ الانتهاء لإعادة تفعيله.");
        Status = SearchRequestStatus.Active;
        // Listings that appeared while it was paused are evaluated without a flood of notifications.
        MatchedUpTo = null;
        UpdatedAt = nowUtc;
    }

    public void Close(DateTime nowUtc)
    {
        if (Status == SearchRequestStatus.Closed)
            return;
        Status = SearchRequestStatus.Closed;
        ClosedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    /// <summary>Moves an active or paused request past its end date to Expired. Returns true when it changed.</summary>
    public bool ExpireIfDue(DateTime nowUtc)
    {
        if (Status is not (SearchRequestStatus.Active or SearchRequestStatus.Paused) || ExpiresAt > nowUtc)
            return false;
        Status = SearchRequestStatus.Expired;
        UpdatedAt = nowUtc;
        return true;
    }

    /// <summary>An expired request can be brought back by giving it a new end date.</summary>
    public void Renew(DateTime? expiresAt, DateTime nowUtc)
    {
        if (Status != SearchRequestStatus.Expired)
            throw new InvalidStateTransitionException("يمكن تجديد الطلبات المنتهية فقط.");
        ExpiresAt = ClampExpiry(expiresAt, nowUtc);
        Status = SearchRequestStatus.Active;
        MatchedUpTo = null;
        UpdatedAt = nowUtc;
    }

    public bool IsActive(DateTime nowUtc) => Status == SearchRequestStatus.Active && ExpiresAt > nowUtc;

    public void MarkMatchedUpTo(DateTime cursorUtc)
    {
        if (MatchedUpTo is null || cursorUtc > MatchedUpTo)
            MatchedUpTo = cursorUtc;
    }

    private void EnsureOpen()
    {
        if (Status is SearchRequestStatus.Closed or SearchRequestStatus.Expired)
            throw new InvalidStateTransitionException("الطلب مغلق أو منتهٍ.");
    }

    private static DateTime ClampExpiry(DateTime? requested, DateTime nowUtc)
    {
        var latest = nowUtc + MaxLifetime;
        var value = requested ?? nowUtc + DefaultLifetime;
        if (value <= nowUtc)
            throw new DomainException("تاريخ انتهاء الطلب يجب أن يكون في المستقبل.");
        return value > latest ? latest : value;
    }

    private void Apply(
        ListingType listingType,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        int? propertyTypeId,
        decimal? minBudget,
        decimal maxBudget,
        string currencyCode,
        int? minRooms,
        decimal? minArea,
        decimal? maxArea,
        bool needsBalcony,
        bool needsElevator,
        bool needsParking,
        bool needsFurnished,
        string? details)
    {
        if (listingType is not (ListingType.ForSale or ListingType.ForRent))
            throw new DomainException("نوع الطلب يجب أن يكون شراء أو إيجار.");
        if (governorateId <= 0)
            throw new DomainException("المحافظة مطلوبة.");
        if (maxBudget <= 0)
            throw new DomainException("الميزانية القصوى مطلوبة.");
        if (minBudget is < 0 || minBudget > maxBudget)
            throw new DomainException("الحد الأدنى للميزانية غير صحيح.");
        if (minArea.HasValue && maxArea.HasValue && minArea > maxArea)
            throw new DomainException("نطاق المساحة غير صحيح.");
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new DomainException("العملة مطلوبة.");

        ListingType = listingType;
        GovernorateId = governorateId;
        DistrictId = districtId;
        NeighborhoodId = neighborhoodId;
        PropertyTypeId = propertyTypeId;
        MinBudget = minBudget;
        MaxBudget = maxBudget;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        MinRooms = minRooms;
        MinArea = minArea;
        MaxArea = maxArea;
        NeedsBalcony = needsBalcony;
        NeedsElevator = needsElevator;
        NeedsParking = needsParking;
        NeedsFurnished = needsFurnished;
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
    }
}
