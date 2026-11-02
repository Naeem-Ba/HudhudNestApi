using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Domain.Growth.Entities;
using MediatR;

namespace HudhudNestApi.Application.Growth;

/// <summary>Growth engine P4: the first publication of a listing (republishing writes nothing new).</summary>
public sealed class ListingPublishedGrowthEventHandler : INotificationHandler<PropertyPublishedEvent>
{
    private readonly IGrowthEventRecorder _growth;

    public ListingPublishedGrowthEventHandler(IGrowthEventRecorder growth) => _growth = growth;

    public Task Handle(PropertyPublishedEvent notification, CancellationToken cancellationToken) =>
        _growth.RecordAsync(
            GrowthEventNames.ListingPublished,
            $"listing_published:{notification.PropertyId:N}",
            notification.PublishedByUserId,
            notification.PropertyId,
            ct: cancellationToken);
}
