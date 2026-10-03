using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Security audit 2026-10-03, finding F-01: <c>PUT /api/properties/{id}</c> binds
/// <see cref="UpdatePropertyCommand"/> straight from the request body, so every field on the
/// record is client-controlled. <c>ExpiresAt</c> is not an owner-editable field -- the listing
/// lifetime is only ever moved by publishing (ListingLifecyclePolicy.PublicationPeriod) or by a
/// paid extension an admin confirms (RequestListingExtension / ConfirmListingExtensionPayment).
/// Letting the owner write it makes the paid extension flow optional.
/// </summary>
public sealed class UpdatePropertyCommandHandlerSecurityTests
{
    // Same options Program.cs registers for the controllers (camelCase + string enums).
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Owner_cannot_move_listing_expiry_through_the_update_body()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("شقة للإيجار", "وصف الشقة", ownerId, ListingType.ForRent);
        var originalExpiry = DateTime.UtcNow.AddDays(-1); // already expired: only a paid extension revives it
        property.ExpiresAt = originalExpiry;

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.GetOwnedPropertyOrThrowAsync(property.Id, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var sut = new UpdatePropertyCommandHandler(
            Mock.Of<IPropertyRepository>(),
            ownership.Object,
            Mock.Of<IPropertyPriceHistoryRepository>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<INotificationService>(),
            Mock.Of<ILocationSuggestionService>(),
            Mock.Of<ILocationHierarchyChecker>(),
            Mock.Of<IPublisher>(),
            NullLogger<UpdatePropertyCommandHandler>.Instance);

        // Exactly what a malicious client sends: one field the UI never offers.
        var body = JsonSerializer.Deserialize<UpdatePropertyCommand>(
            """{ "expiresAt": "2099-01-01T00:00:00Z" }""", BodyOptions)!;
        var command = body with { PropertyId = property.Id, RequestingUserId = ownerId };

        await sut.Handle(command, CancellationToken.None);

        Assert.Equal(originalExpiry, property.ExpiresAt);
    }
}
