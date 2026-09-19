using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;

/// <summary>
/// Handles CreateShortStayListingCommand.
///
/// BUG FIX: this handler used to skip every gate CreatePropertyCommandHandler enforces —
/// no contact-confirmation check, no plan-selection check, and critically no plan-quota
/// check at all, so a Short-Stay listing never consumed the owner's plan quota and an
/// unverified/plan-less account could create unlimited listings via the API directly. This
/// now mirrors CreatePropertyCommandHandler's gates exactly, reading the SAME unified count
/// through <see cref="IActiveListingCounter"/> (Property + Short-Stay summed) so the two
/// creation paths police one shared pool instead of two independent, smaller ones.
/// </summary>
public sealed class CreateShortStayListingCommandHandler
    : IRequestHandler<CreateShortStayListingCommand, ShortStayListingDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IAccommodationTypeRepository _accommodationTypes;
    private readonly IAgencyRepository _agencies;
    private readonly IUserIdentityReadService _identity;
    private readonly IActiveListingCounter _activeListingCounter;
    private readonly IListingQuotaPolicy _quotaPolicy;
    private readonly IPropertyOwnershipService _propertyOwnership;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreateShortStayListingCommandHandler> _logger;

    public CreateShortStayListingCommandHandler(
        IShortStayListingRepository listings,
        IAccommodationTypeRepository accommodationTypes,
        IAgencyRepository agencies,
        IUserIdentityReadService identity,
        IActiveListingCounter activeListingCounter,
        IListingQuotaPolicy quotaPolicy,
        IPropertyOwnershipService propertyOwnership,
        IUnitOfWork uow,
        ILogger<CreateShortStayListingCommandHandler> logger)
    {
        _listings = listings;
        _accommodationTypes = accommodationTypes;
        _agencies = agencies;
        _identity = identity;
        _activeListingCounter = activeListingCounter;
        _quotaPolicy = quotaPolicy;
        _propertyOwnership = propertyOwnership;
        _uow = uow;
        _logger = logger;
    }

    public async Task<ShortStayListingDto> Handle(CreateShortStayListingCommand request, CancellationToken ct)
    {
        var accommodationType = await _accommodationTypes.GetByIdAsync(request.AccommodationTypeId, ct)
            ?? throw new NotFoundException($"AccommodationType {request.AccommodationTypeId} was not found.");

        // Security audit finding (2026-09-18): request.PropertyId used to be passed straight
        // into ShortStayListing.Create with no lookup at all -- any authenticated user could
        // link a Short-Stay listing to a PropertyId they don't own. Reuses the same
        // IPropertyOwnershipService every Property-mutating handler already goes through
        // (UpdatePropertyCommandHandler, UploadPropertyImagesCommandHandler, etc.) rather than
        // writing a second, parallel ownership check.
        if (request.PropertyId is { } propertyId)
            await _propertyOwnership.EnsureOwnerAsync(propertyId, request.OwnerId, "link", ct);

        // Same two gates CreatePropertyCommandHandler enforces before its first listing —
        // see that handler's doc comments for why both apply regardless of role. Checked
        // before the quota's transaction/advisory lock even opens: cheap checks first.
        var ownerAccount = await _agencies.GetUserAccountAsync(request.OwnerId, ct);
        await EnsureContactConfirmedAsync(request.OwnerId, ct);
        var verifiedAccount = EnsurePlanSelected(ownerAccount);

        var listing = await CreateWithQuotaEnforcedAsync(request, verifiedAccount, ct);

        return listing.ToDto(accommodationType);
    }

    /// <summary>
    /// Mirrors CreatePropertyCommandHandler.CreateWithQuotaEnforcedAsync exactly: same
    /// advisory-lock key derivation (owner or agency), same read-then-write race the lock
    /// closes (RELEASE-BLOCKERS-AR.md B-10), same transaction scoping.
    /// </summary>
    private async Task<ShortStayListing> CreateWithQuotaEnforcedAsync(
        CreateShortStayListingCommand request,
        UserAccount ownerAccount,
        CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);

        ShortStayListing listing;
        try
        {
            var lockKey = ownerAccount.AgencyId is { } lockAgencyId
                ? ListingQuotaLock.ForAgency(lockAgencyId)
                : ListingQuotaLock.ForOwner(request.OwnerId);

            await _uow.AcquireAdvisoryLockAsync(lockKey, ct);

            await EnsureListingQuotaAvailableAsync(ownerAccount, ct);

            listing = ShortStayListing.Create(
                request.OwnerId,
                request.AccommodationTypeId,
                request.Title,
                request.Description,
                request.Capacity,
                request.Bedrooms,
                request.Bathrooms,
                request.CheckInTime,
                request.CheckOutTime,
                request.Latitude,
                request.Longitude,
                request.PropertyId,
                request.CurrencyCode);

            var defaultRoomType = new RoomType
            {
                Name = "الوحدة الافتراضية",
                BasePricePerNight = request.DefaultBasePricePerNight,
            };
            defaultRoomType.Units.Add(new AccommodationUnit { Label = "الوحدة الوحيدة" });
            listing.RoomTypes.Add(defaultRoomType);

            await _listings.AddAsync(listing, ct);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }

        return listing;
    }

    /// <summary>See CreatePropertyCommandHandler.EnsureContactConfirmedAsync's doc comment —
    /// identical rule, applied here for parity across listing types.</summary>
    private async Task EnsureContactConfirmedAsync(Guid ownerId, CancellationToken ct)
    {
        var identity = await _identity.FindByIdAsync(ownerId, ct);

        if (identity is { } snapshot && (snapshot.EmailConfirmed || snapshot.PhoneConfirmed))
            return;

        _logger.LogInformation(
            "Short-stay listing creation blocked: neither email nor phone confirmed. OwnerId={OwnerId}",
            ownerId);

        throw new ForbiddenException(
            "أكّد بريدك الإلكتروني أو رقم هاتفك أولاً قبل نشر إعلان.",
            "LISTING_CONTACT_NOT_CONFIRMED");
    }

    /// <summary>See CreatePropertyCommandHandler.EnsurePlanSelected's doc comment — identical
    /// rule, applied here for parity across listing types.</summary>
    private UserAccount EnsurePlanSelected(UserAccount? ownerAccount)
    {
        if (ownerAccount?.PlanId is not null)
            return ownerAccount;

        _logger.LogInformation(
            "Short-stay listing creation blocked: no plan selected. OwnerId={OwnerId}",
            ownerAccount?.Id);

        throw new ForbiddenException(
            "اختر خطة أولاً قبل نشر إعلان — الخطة المجانية متاحة أيضاً.",
            "LISTING_PLAN_REQUIRED");
    }

    /// <summary>
    /// Enforces the SAME active-listing limit CreatePropertyCommandHandler enforces, read
    /// through the same <see cref="IActiveListingCounter"/> total (Property + Short-Stay
    /// summed) — a Short-Stay listing now consumes the identical pool a Sale/Rent listing
    /// does, rather than an unlimited pool of its own.
    /// </summary>
    private async Task EnsureListingQuotaAvailableAsync(UserAccount ownerAccount, CancellationToken ct)
    {
        var limit = await _quotaPolicy.GetActiveListingLimitAsync(ownerAccount, ct);

        if (ownerAccount.AgencyId is { } agencyId)
        {
            var agencyActiveListings = await _activeListingCounter.CountActiveListingsByAgencyAsync(agencyId, ct);

            if (agencyActiveListings < limit)
                return;

            _logger.LogInformation(
                "Short-stay listing creation blocked by agency-pooled quota. AgencyId={AgencyId}, Active={Active}, Limit={Limit}",
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
            "Short-stay listing creation blocked by plan quota. OwnerId={OwnerId}, Active={Active}, Limit={Limit}",
            ownerAccount.Id,
            activeListings,
            limit);

        throw new ConflictException(
            $"خطتكم الحالية تسمح بـ {limit} إعلاناً نشطاً كحد أقصى. لديكم حالياً {activeListings}. " +
            "احذفوا إعلاناً قائماً أو رقّوا خطتكم لإضافة إعلان جديد.");
    }
}
