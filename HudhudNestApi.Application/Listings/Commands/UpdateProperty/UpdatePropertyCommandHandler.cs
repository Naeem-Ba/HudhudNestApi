using HudhudNestApi.Application.Notifications.Localization;
using MediatR;
using Microsoft.Extensions.Logging;
using FluentValidation.Results;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Domain.Enums;

// Phase-0, Task 2: price-history snapshots are written in this handler, in the
// SAME SaveChangesAsync as the property update. Do not move this into the
// NotifyChangesAsync try/catch below — history must persist even if the
// (best-effort) notification push fails.

namespace HudhudNestApi.Application.Listings.Commands.UpdateProperty;

/// <summary>
/// Handles UpdatePropertyCommand.
/// Only applies non-null fields. Ownership is enforced through IPropertyOwnershipService.
/// </summary>
public sealed class UpdatePropertyCommandHandler
    : IRequestHandler<UpdatePropertyCommand, bool>
{
    private readonly IPropertyRepository _repo;
    private readonly IPropertyOwnershipService _ownership;
    private readonly IPropertyPriceHistoryRepository _priceHistory;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILocationSuggestionService _locationSuggestions;
    private readonly ILocationHierarchyChecker _locations;
    private readonly ICommonLookupService _lookups;
    private readonly IPublisher _publisher;
    private readonly ILogger<UpdatePropertyCommandHandler> _logger;
    private readonly IListingQuotaGuard _quotaGuard;
    private readonly IAgencyRepository _agencies;

    public UpdatePropertyCommandHandler(
        IPropertyRepository repo,
        IPropertyOwnershipService ownership,
        IPropertyPriceHistoryRepository priceHistory,
        IUnitOfWork uow,
        INotificationService notifications,
        ILocationSuggestionService locationSuggestions,
        ILocationHierarchyChecker locations,
        ICommonLookupService lookups,
        IPublisher publisher,
        ILogger<UpdatePropertyCommandHandler> logger,
        IListingQuotaGuard quotaGuard,
        IAgencyRepository agencies)
    {
        _quotaGuard = quotaGuard;
        _agencies = agencies;
        _repo = repo;
        _ownership = ownership;
        _priceHistory = priceHistory;
        _uow = uow;
        _notifications = notifications;
        _locationSuggestions = locationSuggestions;
        _locations = locations;
        _lookups = lookups;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<bool> Handle(
        UpdatePropertyCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _ownership.GetOwnedPropertyOrThrowAsync(
            request.PropertyId,
            request.RequestingUserId,
            operation: "update",
            ct: cancellationToken);

        var oldStatus = property.Status;
        var oldColdRent = property.ColdRent;
        var oldWarmRent = property.WarmRent;
        var oldPurchasePrice = property.PurchasePrice;
        var oldIsPublished = property.IsPublished;

        if (request.Title is not null)
            property.UpdateTitle(request.Title);

        if (request.Description is not null)
            property.UpdateDescription(request.Description);

        if (request.Street is not null) property.Street = request.Street;
        if (request.City is not null) property.City = request.City;
        if (request.Region is not null) property.Region = request.Region;
        if (request.PostalCode is not null) property.PostalCode = request.PostalCode;
        if (request.CountryCode is not null) property.CountryCode = request.CountryCode.ToUpperInvariant();
        if (request.CurrencyCode is not null) property.CurrencyCode = request.CurrencyCode.ToUpperInvariant();

        if (request.GovernorateId.HasValue) property.GovernorateId = request.GovernorateId;
        if (request.DistrictId.HasValue) property.DistrictId = request.DistrictId;
        if (request.DistrictText is not null) property.DistrictText = request.DistrictText;
        if (request.NeighborhoodId.HasValue) property.NeighborhoodId = request.NeighborhoodId;
        if (request.NeighborhoodText is not null) property.NeighborhoodText = request.NeighborhoodText;
        if (request.PropertyTypeId.HasValue)
        {
            // An id the catalog does not know used to reach the foreign key and come back as a 500.
            var catalog = await _lookups.GetPropertyTypeCatalogAsync(cancellationToken);
            if (catalog.All(type => type.Id != request.PropertyTypeId.Value))
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("PropertyTypeId", "Unknown property type.")
                });
            }

            property.PropertyTypeId = request.PropertyTypeId;
        }

        // Checked on the merged values (a null id keeps the stored one, so the request alone cannot tell),
        // and only when the request changes the location: a listing saved before this check existed must
        // stay editable in its other fields.
        if ((request.GovernorateId.HasValue || request.DistrictId.HasValue || request.NeighborhoodId.HasValue) &&
            !await _locations.IsConsistentAsync(property.GovernorateId, property.DistrictId, property.NeighborhoodId, cancellationToken))
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Location", "The governorate, district and neighborhood must exist and belong together.")
            });
        }

        if (request.Latitude.HasValue) property.Latitude = request.Latitude;
        if (request.Longitude.HasValue) property.Longitude = request.Longitude;
        if (request.ColdRent.HasValue) property.ColdRent = request.ColdRent;
        if (request.WarmRent.HasValue) property.WarmRent = request.WarmRent;
        if (request.PurchasePrice.HasValue) property.PurchasePrice = request.PurchasePrice;
        if (request.Deposit.HasValue) property.Deposit = request.Deposit;
        if (request.AdditionalCosts.HasValue) property.AdditionalCosts = request.AdditionalCosts;
        if (request.Rooms.HasValue) property.Rooms = request.Rooms;
        if (request.Area.HasValue) property.Area = request.Area;
        if (request.AreaUnit.HasValue) property.AreaUnit = request.AreaUnit.Value;
        if (request.Floor.HasValue) property.Floor = request.Floor;
        if (request.TotalFloors.HasValue) property.TotalFloors = request.TotalFloors;
        if (request.RentalStartDate.HasValue) property.RentalStartDate = request.RentalStartDate;
        if (request.RentalEndDate.HasValue) property.RentalEndDate = request.RentalEndDate;
        if (request.RentalDurationType.HasValue) property.RentalDurationType = request.RentalDurationType;
        if (request.LegalStatus.HasValue) property.LegalStatus = request.LegalStatus.Value;
        if (request.FurnishingStatus.HasValue) property.FurnishingStatus = request.FurnishingStatus.Value;
        if (request.HasBalcony.HasValue) property.HasBalcony = request.HasBalcony.Value;
        if (request.HasElevator.HasValue) property.HasElevator = request.HasElevator.Value;
        if (request.HasParkingSpace.HasValue) property.HasParkingSpace = request.HasParkingSpace.Value;
        if (request.HeatingType.HasValue) property.HeatingType = request.HeatingType.Value;
        if (request.Condition.HasValue) property.Condition = request.Condition.Value;
        if (request.EnergyEfficiency.HasValue) property.EnergyEfficiency = request.EnergyEfficiency.Value;
        if (request.AvailableFrom.HasValue) property.AvailableFrom = request.AvailableFrom;
        // request.ExpiresAt is deliberately NOT applied: the command is bound straight from the
        // PUT body, and the listing lifetime may only move through publishing or a paid, admin-
        // confirmed extension (security audit 2026-10-03, F-01).

        if (request.AmenityIds is not null)
        {
            var distinct = request.AmenityIds.Distinct().ToList();
            var known = (await _lookups.GetAmenitiesAsync(cancellationToken))
                .Select(a => a.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (distinct.Any(id => !known.Contains(id.ToString())))
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("AmenityIds", "Unknown amenity.")
                });
            }

            await _repo.SetAmenitiesAsync(property.Id, distinct, cancellationToken);
        }

        if (request.Status.HasValue)
            property.ChangeStatus(request.Status.Value);

        if (request.IsPublished.HasValue)
        {
            if (request.IsPublished.Value && !property.IsPublished)
                property.Publish();
            else if (!request.IsPublished.Value && property.IsPublished)
                property.Unpublish();
        }

        await RecordPriceChangesAsync(
            property,
            oldColdRent,
            oldWarmRent,
            oldPurchasePrice,
            request.RequestingUserId,
            cancellationToken);

        _repo.Update(property);

        if (ReleasesQuotaSeat(oldStatus) && !ReleasesQuotaSeat(property.Status))
        {
            // Launch audit A5-001: Sold/Rented/Expired do not count against the plan quota, so
            // moving a listing back to an active status takes a seat again. Without this check a
            // free-plan owner could loop "mark Sold, create new, set the old one Available" and
            // publish without limit. Same advisory lock and guard as creation, so a concurrent
            // create cannot take the last seat between the check and the save.
            var owner = await _agencies.GetUserAccountAsync(property.OwnerId, cancellationToken)
                ?? throw new ConflictException("تعذّر العثور على صاحب الإعلان.");

            await _uow.BeginTransactionAsync(cancellationToken);

            try
            {
                await _uow.AcquireAdvisoryLockAsync(
                    owner.AgencyId is { } agencyId
                        ? ListingQuotaLock.ForAgency(agencyId)
                        : ListingQuotaLock.ForOwner(owner.Id),
                    cancellationToken);

                await _quotaGuard.EnsureCapacityAsync(owner, cancellationToken);
                await _uow.SaveChangesAsync(cancellationToken);
                await _uow.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await _uow.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }
        else
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }

        // Phase 11: tell SocialDistribution a status transition actually happened, so it can
        // decide whether any live publication needs updating/archiving. Published AFTER the
        // property row is durably saved, and the subscribing handler (like
        // PropertyPublishedDistributionHandler before it) swallows its own exceptions — a broken
        // rule/publisher must never fail this property update (spec §7).
        if (!Equals(property.Status, oldStatus))
        {
            await _publisher.Publish(
                new PropertyStatusChangedEvent(property.Id, oldStatus, property.Status, DateTime.UtcNow),
                cancellationToken);
        }

        // A draft flipped to published through an edit is the same "listing went live" moment as
        // PATCH /publish — without this the owner's choice of route decided whether the listing
        // was ever auto-distributed. Same fire-and-notify contract: the subscribing handler
        // swallows its own exceptions (see PropertyPublishedDistributionHandler).
        if (property.IsPublished && !oldIsPublished)
        {
            await _publisher.Publish(
                new PropertyPublishedEvent(property.Id, property.PublishedAt ?? DateTime.UtcNow, request.RequestingUserId),
                cancellationToken);
        }

        try
        {
            await NotifyChangesAsync(
                property,
                oldStatus,
                oldColdRent,
                oldWarmRent,
                oldPurchasePrice,
                oldIsPublished,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send property update notification. PropertyId={PropertyId}, OwnerId={OwnerId}",
                property.Id,
                property.OwnerId);
        }

        try
        {
            if (property.DistrictId is null && !string.IsNullOrWhiteSpace(property.DistrictText) && property.GovernorateId.HasValue)
            {
                await _locationSuggestions.SubmitDistrictSuggestionAsync(
                    property.GovernorateId.Value, property.DistrictText, request.RequestingUserId, property.Id, cancellationToken);
            }

            if (property.NeighborhoodId is null && !string.IsNullOrWhiteSpace(property.NeighborhoodText) && property.DistrictId.HasValue)
            {
                await _locationSuggestions.SubmitNeighborhoodSuggestionAsync(
                    property.DistrictId.Value, property.NeighborhoodText, request.RequestingUserId, property.Id, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit location suggestion for property {PropertyId} — update was still saved successfully.",
                property.Id);
        }

        return true;
    }

    /// <summary>
    /// Writes one PropertyPriceHistory row per price field that actually changed.
    /// Queued on the same UnitOfWork as the property update itself (SaveChangesAsync
    /// is called once, after this method returns) so history and the property row
    /// are always consistent — never lands "half saved".
    /// </summary>
    private async Task RecordPriceChangesAsync(
        Property property,
        decimal? oldColdRent,
        decimal? oldWarmRent,
        decimal? oldPurchasePrice,
        Guid changedByUserId,
        CancellationToken cancellationToken)
    {
        if (property.ColdRent != oldColdRent)
        {
            await _priceHistory.AddAsync(
                PropertyPriceHistory.Create(property.Id, nameof(property.ColdRent), oldColdRent, property.ColdRent, property.CurrencyCode, changedByUserId),
                cancellationToken);
        }

        if (property.WarmRent != oldWarmRent)
        {
            await _priceHistory.AddAsync(
                PropertyPriceHistory.Create(property.Id, nameof(property.WarmRent), oldWarmRent, property.WarmRent, property.CurrencyCode, changedByUserId),
                cancellationToken);
        }

        if (property.PurchasePrice != oldPurchasePrice)
        {
            await _priceHistory.AddAsync(
                PropertyPriceHistory.Create(property.Id, nameof(property.PurchasePrice), oldPurchasePrice, property.PurchasePrice, property.CurrencyCode, changedByUserId),
                cancellationToken);
        }
    }

    private async Task NotifyChangesAsync(
        Property property,
        object? oldStatus,
        decimal? oldColdRent,
        decimal? oldWarmRent,
        decimal? oldPurchasePrice,
        bool oldIsPublished,
        CancellationToken cancellationToken)
    {
        var ownerId = property.OwnerId;
        var propertyId = property.Id;
        var title = string.IsNullOrWhiteSpace(property.Title)
            ? string.Empty
            : property.Title;

        var currencyCode = string.IsNullOrWhiteSpace(property.CurrencyCode)
            ? string.Empty
            : property.CurrencyCode;

        if (!Equals(property.Status, oldStatus))
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyStatusChanged,
                detail: NotificationText.Of(NotificationKeys.StatusChangedDetail, ("from", oldStatus), ("to", property.Status)),
                ct: cancellationToken);
        }

        if (property.ColdRent != oldColdRent)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPriceChanged,
                detail: NotificationText.Of(NotificationKeys.ColdRentChangedDetail, ("from", FormatMoney(oldColdRent, currencyCode)), ("to", FormatMoney(property.ColdRent, currencyCode))),
                ct: cancellationToken);
        }

        if (property.WarmRent != oldWarmRent)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPriceChanged,
                detail: NotificationText.Of(NotificationKeys.WarmRentChangedDetail, ("from", FormatMoney(oldWarmRent, currencyCode)), ("to", FormatMoney(property.WarmRent, currencyCode))),
                ct: cancellationToken);
        }

        if (property.PurchasePrice != oldPurchasePrice)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPriceChanged,
                detail: NotificationText.Of(NotificationKeys.PurchasePriceChangedDetail, ("from", FormatMoney(oldPurchasePrice, currencyCode)), ("to", FormatMoney(property.PurchasePrice, currencyCode))),
                ct: cancellationToken);
        }

        if (property.IsPublished && !oldIsPublished)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPublished,
                detail: string.Empty,
                ct: cancellationToken);
        }

        if (!property.IsPublished && oldIsPublished)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyUnpublished,
                detail: string.Empty,
                ct: cancellationToken);
        }
    }

    private static string FormatMoney(decimal? value, string currencyCode)
    {
        if (!value.HasValue)
            return "-";

        return string.IsNullOrWhiteSpace(currencyCode)
            ? value.Value.ToString("0.##")
            : $"{value.Value:0.##} {currencyCode}";
    }

    // Mirrors the "active listing" definition in IActiveListingCounter: these statuses free a seat.
    private static bool ReleasesQuotaSeat(PropertyStatus status)
        => status is PropertyStatus.Sold or PropertyStatus.Rented or PropertyStatus.Expired;
}
