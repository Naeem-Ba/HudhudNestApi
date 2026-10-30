using HudhudNestApi.Application.Agencies.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValidationException = HudhudNestApi.Application.Common.Exceptions.ValidationException;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// PUT /api/properties/{id} used to ignore AmenityIds (only the create path stored them), so amenities
/// ticked while editing a listing were never saved. null keeps the stored set, a list replaces it.
/// </summary>
public sealed class UpdatePropertyAmenitiesTests
{
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid Wifi = Guid.NewGuid();
    private static readonly Guid Pool = Guid.NewGuid();

    private static (UpdatePropertyCommandHandler Sut, Property Property, Guid OwnerId, Mock<IPropertyRepository> Repo) Build()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("شقة للإيجار", "وصف الشقة", ownerId, ListingType.ForRent);

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.GetOwnedPropertyOrThrowAsync(property.Id, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var lookups = new Mock<ICommonLookupService>();
        lookups
            .Setup(x => x.GetAmenitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AmenityLookupDto(Wifi.ToString(), "إنترنت", "Comfort", "wifi"),
                new AmenityLookupDto(Pool.ToString(), "مسبح", "Outdoor", "pool")
            });

        var repo = new Mock<IPropertyRepository>();
        var sut = new UpdatePropertyCommandHandler(
            repo.Object,
            ownership.Object,
            Mock.Of<IPropertyPriceHistoryRepository>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<INotificationService>(),
            Mock.Of<ILocationSuggestionService>(),
            Mock.Of<ILocationHierarchyChecker>(),
            lookups.Object,
            Mock.Of<IPublisher>(),
            NullLogger<UpdatePropertyCommandHandler>.Instance,
            Mock.Of<IListingQuotaGuard>(),
            Mock.Of<IAgencyRepository>());

        return (sut, property, ownerId, repo);
    }

    private static UpdatePropertyCommand Body(string json, Guid propertyId, Guid ownerId)
        => JsonSerializer.Deserialize<UpdatePropertyCommand>(json, BodyOptions)! with { PropertyId = propertyId, RequestingUserId = ownerId };

    [Fact]
    public async Task A_list_replaces_the_stored_amenities()
    {
        var (sut, property, ownerId, repo) = Build();

        await sut.Handle(Body($$"""{ "amenityIds": ["{{Wifi}}", "{{Pool}}", "{{Wifi}}"] }""", property.Id, ownerId), CancellationToken.None);

        repo.Verify(r => r.SetAmenitiesAsync(
            property.Id,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(Wifi) && ids.Contains(Pool)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_empty_list_clears_them()
    {
        var (sut, property, ownerId, repo) = Build();

        await sut.Handle(Body("""{ "amenityIds": [] }""", property.Id, ownerId), CancellationToken.None);

        repo.Verify(r => r.SetAmenitiesAsync(property.Id, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Omitting_the_field_leaves_them_alone()
    {
        var (sut, property, ownerId, repo) = Build();

        await sut.Handle(Body("""{ "title": "عنوان جديد" }""", property.Id, ownerId), CancellationToken.None);

        repo.Verify(r => r.SetAmenitiesAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_amenity_is_a_validation_error_not_a_500()
    {
        var (sut, property, ownerId, repo) = Build();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.Handle(Body($$"""{ "amenityIds": ["{{Guid.NewGuid()}}"] }""", property.Id, ownerId), CancellationToken.None));

        repo.Verify(r => r.SetAmenitiesAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
