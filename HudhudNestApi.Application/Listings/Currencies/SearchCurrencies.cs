namespace HudhudNestApi.Application.Listings.Currencies;

/// <summary>
/// The currencies a visitor can search by: the US dollar, the euro and the Syrian pound. Their
/// rates are pinned in the Currencies table (migration PinSearchCurrencyRates) until a pricing
/// feed replaces the manual values. Any other currency a listing may be advertised in matches only
/// itself and is never converted, because no trustworthy rate is kept for it.
/// </summary>
public static class SearchCurrencies
{
    public const string Usd = "USD";
    public const string Eur = "EUR";
    public const string Syp = "SYP";

    public static readonly IReadOnlyList<string> Codes = new[] { Usd, Eur, Syp };

    public static bool IsSupported(string? code)
        => !string.IsNullOrWhiteSpace(code)
           && Codes.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);

    public const string UnsupportedMessage = "CurrencyCode must be one of USD, EUR, SYP.";
}
