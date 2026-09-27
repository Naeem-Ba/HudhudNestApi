using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Events;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Listings.Commands.CreateProperty;

/// <summary>
/// Handles CreatePropertyCommand.
/// Uses the Property.Create() factory method (enforces domain invariants),
/// then saves via UnitOfWork.
///
/// BUG FIX: Was an empty "internal class CreatePropertyCommandHandler {}" — non-functional.
/// </summary>
public sealed class CreatePropertyCommandHandler
    : IRequestHandler<CreatePropertyCommand, Guid>
{
    private readonly IPropertyRepository _repo;
    private readonly IAgencyRepository _agencies;
    private readonly IUserIdentityReadService _identity;
    private readonly IUnitOfWork _uow;
    private readonly ILocationSuggestionService _locationSuggestions;
    private readonly IListingQuotaPolicy _quotaPolicy;
    private readonly IActiveListingCounter _activeListingCounter;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreatePropertyCommandHandler> _logger;

    public CreatePropertyCommandHandler(
        IPropertyRepository repo,
        IAgencyRepository agencies,
        IUserIdentityReadService identity,
        IUnitOfWork uow,
        ILocationSuggestionService locationSuggestions,
        IListingQuotaPolicy quotaPolicy,
        IActiveListingCounter activeListingCounter,
        IPublisher publisher,
        ILogger<CreatePropertyCommandHandler> logger)
    {
        _repo = repo;
        _agencies = agencies;
        _identity = identity;
        _uow = uow;
        _locationSuggestions = locationSuggestions;
        _quotaPolicy = quotaPolicy;
        _activeListingCounter = activeListingCounter;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<Guid> Handle(
        CreatePropertyCommand request,
        CancellationToken cancellationToken)
    {
        // Read once, up front: both the quota check and the attribution below need the
        // owner's current agency, and reading it twice could observe two different answers
        // if the owner changes agency mid-request.
        var ownerAccount = await _agencies.GetUserAccountAsync(request.OwnerId, cancellationToken);

        // Every account is a "customer" by default and must clear two gates before its
        // first listing — regardless of role (Agent/AgencyOwner/AgencyAgent included, by
        // deliberate decision, not an oversight). Checked before the quota's transaction
        // and advisory lock even open: cheap checks first, no lock taken for a request
        // that's going to be rejected anyway.
        await EnsureContactConfirmedAsync(request.OwnerId, cancellationToken);
        var verifiedAccount = EnsurePlanSelected(ownerAccount);

        var property = await CreateWithQuotaEnforcedAsync(request, verifiedAccount, cancellationToken);

        // Best-effort — a manually-typed district/neighborhood name becomes a
        // suggestion for admin review (see LocationSuggestion's doc comment).
        // Deliberately never allowed to fail the actual listing save: this
        // runs after the property is already committed, and any exception
        // here is swallowed (with a log) rather than surfaced to the user.
        await TrySubmitLocationSuggestionsAsync(property, cancellationToken);

        await TryPublishPropertyPublishedEventAsync(property, cancellationToken);

        return property.Id;
    }

    /// <summary>
    /// RELEASE-BLOCKERS-AR.md B-10: the quota check (count existing, then insert one more) is
    /// a classic read-then-write race — two concurrent requests for the same owner/agency can
    /// both read the same count and both proceed. An advisory lock keyed by owner/agency
    /// (<see cref="ListingQuotaLock"/>) serializes only requests that share a key; unrelated
    /// owners/agencies are unaffected. The lock is transaction-scoped, so it releases itself
    /// on commit or rollback — including if EnsureListingQuotaAvailableAsync throws.
    /// </summary>
    private async Task<Property> CreateWithQuotaEnforcedAsync(
        CreatePropertyCommand request,
        UserAccount ownerAccount,
        CancellationToken cancellationToken)
    {
        await _uow.BeginTransactionAsync(cancellationToken);

        Property property;
        try
        {
            var lockKey = ownerAccount.AgencyId is { } lockAgencyId
                ? ListingQuotaLock.ForAgency(lockAgencyId)
                : ListingQuotaLock.ForOwner(request.OwnerId);

            await _uow.AcquireAdvisoryLockAsync(lockKey, cancellationToken);

            await EnsureListingQuotaAvailableAsync(ownerAccount, cancellationToken);

            property = BuildProperty(request, ownerAccount);

            await _repo.AddAsync(property, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            await _uow.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        return property;
    }

    private static Property BuildProperty(
        CreatePropertyCommand request,
        UserAccount ownerAccount)
    {
        // Factory method — the ONLY correct way to create a Property.
        // Throws DomainException if invariants are violated.
        var property = Property.Create(
            title: request.Title,
            description: request.Description,
            ownerId: request.OwnerId,
            listingType: request.ListingType,
            countryCode: request.CountryCode,
            currencyCode: request.CurrencyCode);

        // Set optional fields (public setters — no invariants to enforce)
        property.Street = request.Street;
        property.City = request.City;
        property.Region = request.Region;
        property.PostalCode = request.PostalCode;
        property.GovernorateId = request.GovernorateId;
        property.DistrictId = request.DistrictId;
        property.DistrictText = request.DistrictText;
        property.NeighborhoodId = request.NeighborhoodId;
        property.NeighborhoodText = request.NeighborhoodText;
        property.PropertyTypeId = request.PropertyTypeId;
        property.Latitude = request.Latitude;
        property.Longitude = request.Longitude;
        property.ColdRent = request.ColdRent;
        property.WarmRent = request.WarmRent;
        property.PurchasePrice = request.PurchasePrice;
        property.Deposit = request.Deposit;
        property.AdditionalCosts = request.AdditionalCosts;
        property.Rooms = request.Rooms;
        property.Area = request.Area;
        property.Floor = request.Floor;
        property.HasBalcony = request.HasBalcony;
        property.HasElevator = request.HasElevator;
        property.HasParkingSpace = request.HasParkingSpace;
        property.HeatingType = request.HeatingType;
        property.AvailableFrom = request.AvailableFrom;
        property.AreaUnit = request.AreaUnit;
        property.RentalStartDate = request.RentalStartDate;
        property.RentalEndDate = request.RentalEndDate;
        property.RentalDurationType = request.RentalDurationType;

        // LegalStatus/FurnishingStatus already default sensibly on the entity
        // (Unknown/Unfurnished) — only overwrite when the request actually
        // specifies a value, so an omitted field doesn't silently downgrade a
        // future entity-level default.
        if (request.LegalStatus.HasValue)
            property.LegalStatus = request.LegalStatus.Value;

        if (request.FurnishingStatus.HasValue)
            property.FurnishingStatus = request.FurnishingStatus.Value;

        // Associate amenities if provided
        if (request.AmenityIds is { Count: > 0 })
        {
            foreach (var amenityId in request.AmenityIds)
            {
                property.PropertyAmenities.Add(new PropertyAmenity
                {
                    PropertyId = property.Id,
                    AmenityId = amenityId
                });
            }
        }

        // Attribute the listing to the owner's agency, if they belong to one. Read from the
        // owner's account (fetched up front, above) rather than accepted from the request: a
        // client that could name the agency could attribute its listing to somebody else's
        // office.
        //
        // Attribution is captured once, at creation. It is NOT re-derived later, so a
        // listing keeps the badge of the agency it was published under even if its owner
        // moves on — which is what the listing's history actually was.
        if (ownerAccount.AgencyId is { } agencyId)
        {
            property.SetAgency(agencyId);
        }

        return property;
    }

    /// <summary>
    /// A brand-new account cannot publish until it has confirmed some way to reach the
    /// person behind it — email OR phone, either is enough. Checked against the Identity
    /// snapshot (the source of truth for confirmation state — see IdentityAccountSnapshot's
    /// doc comment), not any cached flag on UserAccount.
    /// </summary>
    private async Task EnsureContactConfirmedAsync(Guid ownerId, CancellationToken ct)
    {
        var identity = await _identity.FindByIdAsync(ownerId, ct);

        if (identity is { } snapshot && (snapshot.EmailConfirmed || snapshot.PhoneConfirmed))
            return;

        _logger.LogInformation(
            "Listing creation blocked: neither email nor phone confirmed. OwnerId={OwnerId}",
            ownerId);

        throw new ForbiddenException(
            "أكّد بريدك الإلكتروني أو رقم هاتفك أولاً قبل نشر إعلان.",
            "LISTING_CONTACT_NOT_CONFIRMED");
    }

    /// <summary>
    /// A brand-new account cannot publish until it has explicitly chosen a plan —
    /// including the free plan. This is a deliberate product decision: "free" is not an
    /// implicit default here, unlike the aspirational note in
    /// FRONTEND_BACKEND_CONTRACT.md §11.4 ("free is not a purchased subscription"). See
    /// UserAccount.PlanId's doc comment.
    /// </summary>
    private UserAccount EnsurePlanSelected(UserAccount? ownerAccount)
    {
        if (ownerAccount?.PlanId is not null)
            return ownerAccount;

        _logger.LogInformation(
            "Listing creation blocked: no plan selected. OwnerId={OwnerId}",
            ownerAccount?.Id);

        throw new ForbiddenException(
            "اختر خطة أولاً قبل نشر إعلان — الخطة المجانية متاحة أيضاً.",
            "LISTING_PLAN_REQUIRED");
    }

    /// <summary>
    /// Enforces the active-listing limit — plan-driven for both an independent owner and an
    /// agency-pooled owner (BACKEND-ISSUES.md §B-3): <see cref="IListingQuotaPolicy"/>
    /// resolves the limit from <c>Plan.ListingLimit</c> via <c>UserAccount.PlanId</c> (the
    /// agency owner's, when <paramref name="ownerAccount"/> belongs to one — see
    /// IListingQuotaPolicy's doc comment), never from a constant here.
    ///
    /// The count itself comes from <see cref="IActiveListingCounter"/> — a shared total
    /// across every listing type that draws from this same quota (Property + ShortStayListing
    /// today), not just this repository's own rows. Without this, a Property and a Short-Stay
    /// listing would each be checked against a separate, smaller pool instead of the one the
    /// owner's plan actually promises.
    ///
    /// Consequence worth being explicit about: an existing owner (or agency) who already
    /// holds more than the limit — e.g. after a plan downgrade — keeps every listing they
    /// have; nothing is retroactively removed. They simply cannot create another until they
    /// are back under it.
    /// </summary>
    private async Task EnsureListingQuotaAvailableAsync(
        UserAccount ownerAccount,
        CancellationToken ct)
    {
        var limit = await _quotaPolicy.GetActiveListingLimitAsync(ownerAccount, ct);

        if (ownerAccount.AgencyId is { } agencyId)
        {
            var agencyActiveListings = await _activeListingCounter.CountActiveListingsByAgencyAsync(agencyId, ct);

            if (agencyActiveListings < limit)
                return;

            _logger.LogInformation(
                "Listing creation blocked by agency-pooled quota. AgencyId={AgencyId}, Active={Active}, Limit={Limit}",
                agencyId,
                agencyActiveListings,
                limit);

            throw new ConflictException(
                $"بلغ مكتبكم الحدّ الأقصى المسموح به من الإعلانات النشطة ({limit} إعلاناً) وفق خطة مكتبكم. " +
                "احذفوا إعلاناً قائماً أو رقّوا الخطة لإضافة إعلان جديد.");
        }

        var activeListings = await _activeListingCounter.CountActiveListingsByOwnerAsync(ownerAccount.Id, ct);

        if (activeListings < limit)
            return;

        _logger.LogInformation(
            "Listing creation blocked by plan quota. OwnerId={OwnerId}, Active={Active}, Limit={Limit}",
            ownerAccount.Id,
            activeListings,
            limit);

        throw new ConflictException(
            $"خطتكم الحالية تسمح بـ {limit} إعلاناً نشطاً كحد أقصى. لديكم حالياً {activeListings}. " +
            "احذفوا إعلاناً قائماً أو رقّوا خطتكم لإضافة إعلان جديد.");
    }

    /// <summary>
    /// A listing created through the app is live immediately (Property.Create defaults to
    /// published), so this is the "listing published" moment SocialDistribution has to hear
    /// about — previously only the explicit PATCH /publish (drafts) raised it, so a new listing
    /// was never auto-distributed. Raised strictly AFTER the commit and, like the location
    /// suggestions above, best-effort: the listing already exists, and failing the request over
    /// a side effect would just make the owner retry and create a duplicate. (The reconciliation
    /// sweep in SocialDistribution is the safety net for a notification that never arrives.)
    /// </summary>
    private async Task TryPublishPropertyPublishedEventAsync(Property property, CancellationToken ct)
    {
        if (!property.IsPublished)
            return;

        try
        {
            await _publisher.Publish(
                new PropertyPublishedEvent(property.Id, property.PublishedAt ?? DateTime.UtcNow, property.OwnerId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to publish PropertyPublishedEvent for a newly created listing. PropertyId={PropertyId}",
                property.Id);
        }
    }

    private async Task TrySubmitLocationSuggestionsAsync(Property property, CancellationToken ct)
    {
        try
        {
            if (property.DistrictId is null && !string.IsNullOrWhiteSpace(property.DistrictText) && property.GovernorateId.HasValue)
            {
                await _locationSuggestions.SubmitDistrictSuggestionAsync(
                    property.GovernorateId.Value, property.DistrictText, property.OwnerId, property.Id, ct);
            }

            if (property.NeighborhoodId is null && !string.IsNullOrWhiteSpace(property.NeighborhoodText) && property.DistrictId.HasValue)
            {
                await _locationSuggestions.SubmitNeighborhoodSuggestionAsync(
                    property.DistrictId.Value, property.NeighborhoodText, property.OwnerId, property.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit location suggestion for property {PropertyId} — listing was still saved successfully.",
                property.Id);
        }
    }
}

