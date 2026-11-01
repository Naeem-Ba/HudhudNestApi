using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Pricing;
using HudhudNestApi.Configuration;
using HudhudNestApi.Domain.Pricing;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Growth engine P6: statistical price ranges from live asking prices (never a valuation). Anonymous, rate
/// limited and cached; the data changes only when the background rebuild runs. Methodology:
/// docs/growth-engine/PRICE-METHODOLOGY.md.
/// </summary>
[ApiController]
[Route("api/price-estimates")]
[AllowAnonymous]
public sealed class PriceEstimatesController : ControllerBase
{
    private readonly ISender _sender;

    public PriceEstimatesController(ISender sender) => _sender = sender;

    /// <summary>Range for one property: kind (Sale/Rent), governorate, optional district and type, area in m².</summary>
    [HttpGet]
    [EnableRateLimiting("public-read")]
    [OutputCache(PolicyName = OutputCacheRegistration.PriceEstimatesPolicy)]
    [ProducesResponseType(typeof(PriceEstimateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Estimate(
        [FromQuery] PriceKind kind, [FromQuery] int governorateId, [FromQuery] int? districtId,
        [FromQuery] int? propertyTypeId, [FromQuery] decimal area, [FromQuery] string? currency, CancellationToken ct)
        => Ok(await _sender.Send(new GetPriceEstimateQuery(kind, governorateId, districtId, propertyTypeId, area, currency), ct));

    /// <summary>Per-m² quartiles for the governorate and its districts that have enough listings.</summary>
    [HttpGet("areas")]
    [EnableRateLimiting("public-read")]
    [OutputCache(PolicyName = OutputCacheRegistration.PriceEstimatesPolicy)]
    [ProducesResponseType(typeof(IReadOnlyList<AreaPriceRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AreaPrices(
        [FromQuery] int governorateId, [FromQuery] PriceKind kind, [FromQuery] string? currency, CancellationToken ct)
        => Ok(await _sender.Send(new GetAreaPricesQuery(governorateId, kind, currency), ct));
}
