using HudhudNestApi.Domain.Lookups.Constants;

namespace HudhudNestApi.Application.Listings.Currencies;

/// <summary>
/// The currencies a visitor can search by: the dollar, the euro and the Syrian pound, the same
/// three listings are priced in (<see cref="MarketCurrencies"/>). Their rates are pinned in the
/// Currencies table (migration PinSearchCurrencyRates) until a pricing feed replaces the manual
/// values. A listing still advertised in another currency from before this rule matches only
/// itself and is never converted, because no trustworthy rate is kept for it.
/// </summary>
public static class SearchCurrencies
{
    public static IReadOnlyList<string> Codes => MarketCurrencies.Codes;

    public static bool IsSupported(string? code) => MarketCurrencies.IsSupported(code);

    public const string UnsupportedMessage = "CurrencyCode must be one of USD, EUR, SYP.";
}
