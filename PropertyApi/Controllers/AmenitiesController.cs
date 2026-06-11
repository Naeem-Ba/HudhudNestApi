using MediatR;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Amenities.Queries.GetAmenities;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/amenities")]
public sealed class AmenitiesController : ControllerBase
{
    private readonly ISender _sender;

    public AmenitiesController(ISender sender)
        => _sender = sender;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var amenities = await _sender.Send(new GetAmenitiesQuery(), ct);
        return Ok(amenities);
    }
}
