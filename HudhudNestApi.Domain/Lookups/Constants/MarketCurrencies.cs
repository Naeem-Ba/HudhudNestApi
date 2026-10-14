namespace HudhudNestApi.Domain.Lookups.Constants;

/// <summary>
/// The only currencies the product prices and searches in: the US dollar, the euro and the Syrian
/// pound. Listings (sale, rent, short stay), price searches and saved searches all read this one
/// list. Their exchange rates are pinned in the Currencies table until a pricing feed exists.
/// </summary>
public static class MarketCurrencies
{
    public const string Usd = "USD";
    public const string Eur = "EUR";
    public const string Syp = "SYP";

    public static readonly IReadOnlyList<string> Codes = new[] { Usd, Eur, Syp };

    public static readonly IReadOnlySet<string> CodeSet =
        new HashSet<string>(Codes, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? code)
        => !string.IsNullOrWhiteSpace(code) && CodeSet.Contains(code.Trim());

    public const string UnsupportedMessage = "Currency must be one of USD, EUR, SYP.";
}
