using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Notifications.Enums;

// Phase-0, Task 2: price-history snapshots are written in this handler, in the
// SAME SaveChangesAsync as the property update. Do not move this into the
// NotifyChangesAsync try/catch below — history must persist even if the
// (best-effort) notification push fails.

namespace PropertyApi.Application.Listings.Commands.UpdateProperty;

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
    private readonly ILogger<UpdatePropertyCommandHandler> _logger;

    public UpdatePropertyCommandHandler(
        IPropertyRepository repo,
        IPropertyOwnershipService ownership,
        IPropertyPriceHistoryRepository priceHistory,
        IUnitOfWork uow,
        INotificationService notifications,
        ILocationSuggestionService locationSuggestions,
        ILogger<UpdatePropertyCommandHandler> logger)
    {
        _repo = repo;
        _ownership = ownership;
        _priceHistory = priceHistory;
        _uow = uow;
        _notifications = notifications;
        _locationSuggestions = locationSuggestions;
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
        if (request.PropertyTypeId.HasValue) property.PropertyTypeId = request.PropertyTypeId;

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
        if (request.ExpiresAt.HasValue) property.ExpiresAt = request.ExpiresAt;

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
        await _uow.SaveChangesAsync(cancellationToken);

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
            ? "العقار"
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
                detail: $"{oldStatus} → {property.Status}",
                ct: cancellationToken);
        }

        if (property.ColdRent != oldColdRent)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPriceChanged,
                detail: $"Cold rent: {FormatMoney(oldColdRent, currencyCode)} → {FormatMoney(property.ColdRent, currencyCode)}",
                ct: cancellationToken);
        }

        if (property.WarmRent != oldWarmRent)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPriceChanged,
                detail: $"Warm rent: {FormatMoney(oldWarmRent, currencyCode)} → {FormatMoney(property.WarmRent, currencyCode)}",
                ct: cancellationToken);
        }

        if (property.PurchasePrice != oldPurchasePrice)
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: ownerId,
                propertyId: propertyId,
                propertyTitle: title,
                type: NotificationType.PropertyPriceChanged,
                detail: $"Purchase price: {FormatMoney(oldPurchasePrice, currencyCode)} → {FormatMoney(property.PurchasePrice, currencyCode)}",
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
}
