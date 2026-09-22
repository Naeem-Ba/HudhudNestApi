using HudhudNestApi.Application.Amenities.Queries.GetAmenities;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Auth.Tests.Application.Handlers;

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
