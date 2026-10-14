namespace HudhudNestApi.Application.Currencies.DTOs;

/// <summary>
/// A currency the product prices in. <see cref="ExchangeRateToUsd"/> is units of this currency per
/// one US dollar (the base), maintained by an administrator; clients use it to show approximate
/// equivalents and the search uses it to compare prices advertised in different currencies.
/// </summary>
public sealed record CurrencyDto(
    string Code,
    string NameEn,
    string NameAr,
    string Symbol,
    int DecimalPlaces,
    decimal ExchangeRateToUsd,
    DateTime RateUpdatedAt,
    bool IsBase);
