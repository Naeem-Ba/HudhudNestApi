using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

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
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAmenities(CancellationToken ct)
        => Ok(await _lookups.GetAmenitiesAsync(ct));

    [HttpGet("categories")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
        => Ok(await _lookups.GetCategoriesAsync(ct));

    [HttpGet("property-types")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyTypes(CancellationToken ct)
        => Ok(await _lookups.GetPropertyTypesAsync(ct));

    [HttpGet("cities")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken ct)
        => Ok(await _lookups.GetCitiesAsync(ct));

    // Phase-0, Task 0 follow-up: real, DB-backed structured-location catalog.
    // Governorate/District/Neighborhood/PropertyType entities and seed data
    // already existed, but nothing exposed them until now — see
    // ICommonLookupService for the full explanation.

    [HttpGet("governorates")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGovernorates([FromQuery] string countryCode, CancellationToken ct)
        => Ok(await _lookups.GetGovernoratesAsync(string.IsNullOrWhiteSpace(countryCode) ? "SY" : countryCode, ct));

    [HttpGet("districts")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDistricts([FromQuery] int governorateId, CancellationToken ct)
    {
        if (governorateId <= 0)
            return BadRequest("governorateId is required.");

        return Ok(await _lookups.GetDistrictsAsync(governorateId, ct));
    }

    [HttpGet("neighborhoods")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNeighborhoods([FromQuery] int districtId, CancellationToken ct)
    {
        if (districtId <= 0)
            return BadRequest("districtId is required.");

        return Ok(await _lookups.GetNeighborhoodsAsync(districtId, ct));
    }

    /// <summary>
    /// The real PropertyType table (Code/NameAr/NameEn/Category/Icon), distinct
    /// from GET /property-types above (a hardcoded, unrelated 5-item catalog —
    /// kept as-is for backward compatibility since nothing consumes it today,
    /// see LookupsApiService on the frontend).
    /// </summary>
    [HttpGet("property-type-catalog")]
    [AllowAnonymous] // Public reference data.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyTypeCatalog(CancellationToken ct)
        => Ok(await _lookups.GetPropertyTypeCatalogAsync(ct));

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
