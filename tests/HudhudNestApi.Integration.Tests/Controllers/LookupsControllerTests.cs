using Microsoft.AspNetCore.Mvc;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Controllers;

/// <summary>QA phase 6 (P6-D03): an unknown cache key is a client mistake (400), not a 500.</summary>
[Trait("Feature", "Lookups")]
public sealed class LookupsControllerTests
{
    [Fact(DisplayName = "Refresh with an unsupported cache key answers 400, not an unhandled exception")]
    public async Task Refresh_UnknownKey_ReturnsBadRequest()
    {
        var lookups = new Mock<ICommonLookupService>();
        lookups.Setup(x => x.RefreshAsync("nope", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Unsupported lookup cache key 'nope'."));

        var response = await new LookupsController(lookups.Object).Refresh("nope", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
    }

    [Fact(DisplayName = "Refresh with a known cache key answers 204")]
    public async Task Refresh_KnownKey_ReturnsNoContent()
    {
        var response = await new LookupsController(Mock.Of<ICommonLookupService>())
            .Refresh("amenities", CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
    }
}
