using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Currencies.Commands.UpsertCurrency;
using HudhudNestApi.Application.Currencies.Queries.GetCurrencies;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Currencies the product prices in, with the exchange rates the search and the clients use to
/// compare prices advertised in different currencies (QA Phase 7, P7-D01).
/// </summary>
[ApiController]
public sealed class CurrenciesController : ControllerBase
{
    private readonly ISender _sender;

    public CurrenciesController(ISender sender) => _sender = sender;

    /// <summary>Public reference data: active currencies, base first.</summary>
    [HttpGet("api/lookups/currencies")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        // Rates change when an administrator edits them, a few times a month at most.
        Response.GetTypedHeaders().CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromMinutes(5)
        };

        return Ok(await _sender.Send(new GetCurrenciesQuery(), ct));
    }

    /// <summary>Creates a currency or sets the rate of an existing one (administrators only).</summary>
    [HttpPut("api/admin/currencies/{code}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Upsert(string code, [FromBody] UpsertCurrencyRequest request, CancellationToken ct)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("userId");
        if (!Guid.TryParse(raw, out var userId))
            return Unauthorized();

        var result = await _sender.Send(
            new UpsertCurrencyCommand(
                code, request.NameEn, request.NameAr, request.Symbol, request.DecimalPlaces,
                request.ExchangeRateToUsd, request.IsActive ?? true, userId,
                HttpContext.Connection.RemoteIpAddress?.ToString()),
            ct);

        return Ok(result);
    }
}

public sealed record UpsertCurrencyRequest(
    string NameEn,
    string NameAr,
    string Symbol,
    int DecimalPlaces,
    decimal ExchangeRateToUsd,
    bool? IsActive);
