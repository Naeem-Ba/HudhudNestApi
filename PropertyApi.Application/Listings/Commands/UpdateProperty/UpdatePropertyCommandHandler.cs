using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Listings.Commands.UpdateProperty;

/// <summary>
/// Handles UpdatePropertyCommand.
/// Only applies non-null fields. Checks ownership before updating.
/// </summary>
public sealed class UpdatePropertyCommandHandler
    : IRequestHandler<UpdatePropertyCommand, bool>
{
    private readonly IPropertyRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public UpdatePropertyCommandHandler(
        IPropertyRepository repo,
        IUnitOfWork uow,
        INotificationService notifications)
    {
        _repo = repo;
        _uow = uow;
        _notifications = notifications;
    }

    public async Task<bool> Handle(
        UpdatePropertyCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _repo.GetByIdAsync(
            request.PropertyId,
            cancellationToken);

        if (property is null)
            return false; // 404 — controller handles response

        // Authorization check: only the owner can edit their listing.
        if (property.OwnerId != request.RequestingUserId)
            throw new UnauthorizedAccessException(
                "You are not authorized to update this listing.");

        // Capture old values before applying changes.
        // These values are used to decide which notifications should be created.
        var oldStatus = property.Status;
        var oldColdRent = property.ColdRent;
        var oldWarmRent = property.WarmRent;
        var oldPurchasePrice = property.PurchasePrice;
        var oldIsPublished = property.IsPublished;

        // Apply only non-null fields — use domain methods for guarded fields.
        if (request.Title is not null)
            property.UpdateTitle(request.Title);

        if (request.Description is not null)
            property.UpdateDescription(request.Description);

        if (request.Street is not null)
            property.Street = request.Street;

        if (request.City is not null)
            property.City = request.City;

        if (request.Region is not null)
            property.Region = request.Region;

        if (request.PostalCode is not null)
            property.PostalCode = request.PostalCode;

        if (request.CountryCode is not null)
            property.CountryCode = request.CountryCode.ToUpperInvariant();

        if (request.CurrencyCode is not null)
            property.CurrencyCode = request.CurrencyCode.ToUpperInvariant();

        if (request.Latitude.HasValue)
            property.Latitude = request.Latitude;

        if (request.Longitude.HasValue)
            property.Longitude = request.Longitude;

        if (request.ColdRent.HasValue)
            property.ColdRent = request.ColdRent;

        if (request.WarmRent.HasValue)
            property.WarmRent = request.WarmRent;

        if (request.PurchasePrice.HasValue)
            property.PurchasePrice = request.PurchasePrice;

        if (request.Deposit.HasValue)
            property.Deposit = request.Deposit;

        if (request.AdditionalCosts.HasValue)
            property.AdditionalCosts = request.AdditionalCosts;

        if (request.Rooms.HasValue)
            property.Rooms = request.Rooms;

        if (request.Area.HasValue)
            property.Area = request.Area;

        if (request.Floor.HasValue)
            property.Floor = request.Floor;

        if (request.TotalFloors.HasValue)
            property.TotalFloors = request.TotalFloors;

        if (request.HasBalcony.HasValue)
            property.HasBalcony = request.HasBalcony.Value;

        if (request.HasElevator.HasValue)
            property.HasElevator = request.HasElevator.Value;

        if (request.HasParkingSpace.HasValue)
            property.HasParkingSpace = request.HasParkingSpace.Value;

        if (request.HeatingType.HasValue)
            property.HeatingType = request.HeatingType.Value;

        if (request.Condition.HasValue)
            property.Condition = request.Condition.Value;

        if (request.EnergyEfficiency.HasValue)
            property.EnergyEfficiency = request.EnergyEfficiency.Value;

        if (request.AvailableFrom.HasValue)
            property.AvailableFrom = request.AvailableFrom;

        if (request.ExpiresAt.HasValue)
            property.ExpiresAt = request.ExpiresAt;

        // Status uses domain method (guards valid transitions).
        if (request.Status.HasValue)
            property.ChangeStatus(request.Status.Value);

        // Publish / Unpublish via domain methods.
        if (request.IsPublished.HasValue)
        {
            if (request.IsPublished.Value && !property.IsPublished)
                property.Publish();
            else if (!request.IsPublished.Value && property.IsPublished)
                property.Unpublish();
        }

        _repo.Update(property);
        await _uow.SaveChangesAsync(cancellationToken);

        // Notifications are non-critical:
        // updating the property has already succeeded, so notification failure must not fail the command.
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
        catch
        {
            // Intentionally ignored.
            // Later we can inject ILogger<UpdatePropertyCommandHandler> and log this.
        }

        return true;
    }

    private async Task NotifyChangesAsync(
        dynamic property,
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