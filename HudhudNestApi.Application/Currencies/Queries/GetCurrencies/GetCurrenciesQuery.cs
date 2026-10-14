using MediatR;
using HudhudNestApi.Application.Currencies.DTOs;
using HudhudNestApi.Application.Currencies.Interfaces;

namespace HudhudNestApi.Application.Currencies.Queries.GetCurrencies;

/// <summary>The active currencies and their rates, for the currency selector and price conversion.</summary>
public sealed record GetCurrenciesQuery : IRequest<IReadOnlyList<CurrencyDto>>;

public sealed class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, IReadOnlyList<CurrencyDto>>
{
    private readonly ICurrencyRepository _currencies;

    public GetCurrenciesQueryHandler(ICurrencyRepository currencies) => _currencies = currencies;

    public async Task<IReadOnlyList<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken ct)
    {
        var currencies = await _currencies.GetActiveAsync(ct);

        return currencies
            .OrderByDescending(c => c.IsBaseCurrency)
            .ThenBy(c => c.Code.Trim(), StringComparer.Ordinal)
            .Select(c => new CurrencyDto(
                // The column is fixed-width (char(5)), so PostgreSQL returns the code space-padded.
                c.Code.Trim(), c.NameEn, c.NameAr, c.Symbol, c.DecimalPlaces,
                c.ExchangeRateToUSD, c.ExchangeRateUpdatedAt, c.IsBaseCurrency))
            .ToList();
    }
}
