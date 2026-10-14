namespace HudhudNestApi.Application.Listings.Currencies;

/// <summary>
/// Exchange rates as stored in the Currencies table: units of each currency per one US dollar
/// (the base). Used to compare and order listing prices that were advertised in different
/// currencies (QA Phase 7, P7-D01).
///
/// A currency with no rate on file cannot be converted. It still matches when it IS the
/// currency the visitor chose (no conversion needed) but never by equivalence, because claiming
/// that a price in an unpriced currency lies inside someone's budget would be a guess.
/// </summary>
public sealed class CurrencyRates
{
    public static readonly CurrencyRates Empty = new(new Dictionary<string, decimal>());

    private readonly IReadOnlyDictionary<string, decimal> _unitsPerUsd;

    public CurrencyRates(IReadOnlyDictionary<string, decimal> unitsPerUsd)
    {
        ArgumentNullException.ThrowIfNull(unitsPerUsd);

        var normalized = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, rate) in unitsPerUsd)
        {
            if (!string.IsNullOrWhiteSpace(code) && rate > 0)
                normalized[code.Trim().ToUpperInvariant()] = rate;
        }

        _unitsPerUsd = normalized;
    }

    public IReadOnlyDictionary<string, decimal> UnitsPerUsd => _unitsPerUsd;

    public bool TryGetRate(string? code, out decimal unitsPerUsd)
    {
        unitsPerUsd = 0m;
        return !string.IsNullOrWhiteSpace(code) && _unitsPerUsd.TryGetValue(code.Trim().ToUpperInvariant(), out unitsPerUsd);
    }

    /// <summary>Converts an amount between two currencies, or null when either has no rate.</summary>
    public decimal? Convert(decimal amount, string fromCode, string toCode)
    {
        if (string.Equals(fromCode, toCode, StringComparison.OrdinalIgnoreCase))
            return amount;

        if (!TryGetRate(fromCode, out var from) || !TryGetRate(toCode, out var to))
            return null;

        return amount * to / from;
    }
}
