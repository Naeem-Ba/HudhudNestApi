using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/lookups")]
public sealed class LookupsController : ControllerBase
{
    private readonly ICommonLookupService _lookups;

    public LookupsController(ICommonLookupService lookups)
    {
        _lookups = lookups;
    }

    [HttpGet("amenities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAmenities(CancellationToken ct)
        => Ok(await _lookups.GetAmenitiesAsync(ct));

    [HttpGet("categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
        => Ok(await _lookups.GetCategoriesAsync(ct));

    [HttpGet("property-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyTypes(CancellationToken ct)
        => Ok(await _lookups.GetPropertyTypesAsync(ct));

    [HttpGet("cities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken ct)
        => Ok(await _lookups.GetCitiesAsync(ct));

    [HttpPost("refresh/{key}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refresh(string key, CancellationToken ct)
    {
        await _lookups.RefreshAsync(key, ct);
        return NoContent();
    }

    [HttpPost("refresh")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RefreshAll(CancellationToken ct)
    {
        await _lookups.RefreshAllAsync(ct);
        return NoContent();
    }
}
