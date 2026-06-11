using PropertyApi.Application.Amenities.Queries.GetAmenities;
using PropertyApi.Application.Common.DTOs;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Auth.Tests.Application.Handlers;

public sealed class AmenitiesHandlerTests
{
    [Fact(DisplayName = "Amenities query handler reads common lookup service cache abstraction")]
    public async Task GetAmenities_Uses_CommonLookupService()
    {
        var lookups = new Mock<ICommonLookupService>();
        lookups.Setup(x => x.GetAmenitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AmenityLookupDto(Guid.NewGuid().ToString(), "WiFi", "Comfort", "wifi")
            ]);

        var handler = new GetAmenitiesQueryHandler(lookups.Object);

        var result = await handler.Handle(new GetAmenitiesQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("WiFi", result[0].Name);
        Assert.Equal("Comfort", result[0].Category);
        lookups.Verify(x => x.GetAmenitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
