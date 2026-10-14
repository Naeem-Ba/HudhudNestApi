using System.Linq.Expressions;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Currencies;

/// <summary>
/// The price rules of the public search, written once as expression trees so the same rule can
/// run as SQL (EF Core) and in memory (unit tests) — and so the price filter and the price sort
/// can never disagree about which number is "the price" of a listing.
/// </summary>
public static class ListingPriceExpressions
{
    /// <summary>
    /// The price a visitor comparing listings of this type sees: the sale price for sale,
    /// the cold rent (falling back to warm rent) for rent. Null when the listing has none, and
    /// null for an unspecified type, where "the price" is not one comparable number
    /// (sale prices and monthly rents are different quantities).
    /// </summary>
    public static Expression<Func<Property, decimal?>> EffectivePrice(ListingType listingType) => listingType switch
    {
        ListingType.ForSale => p => p.PurchasePrice,
        ListingType.ForRent => p => p.ColdRent ?? p.WarmRent,
        _ => p => p.PurchasePrice ?? p.ColdRent ?? p.WarmRent
    };

    /// <summary>
    /// Effective price converted to US dollars, for ordering. A listing whose currency has no
    /// rate cannot be placed (treating it as dollars would be a guess), so it yields null and
    /// sorts last together with the listings that have no price at all.
    /// </summary>
    public static Expression<Func<Property, decimal?>> EffectivePriceInUsd(ListingType listingType, CurrencyRates rates)
    {
        var price = EffectivePrice(listingType);
        var parameter = price.Parameters[0];

        // No rates on file at all (an unseeded database): order by the numbers as written
        // rather than collapsing every listing to "no price".
        if (rates.UnitsPerUsd.Count == 0)
            return price;

        Expression result = Expression.Constant(null, typeof(decimal?));
        foreach (var (code, rate) in rates.UnitsPerUsd)
        {
            var codeEquals = Expression.Equal(
                Expression.Property(parameter, nameof(Property.CurrencyCode)),
                Expression.Constant(code));

            // price / rate, kept nullable so a missing price stays missing.
            var converted = Expression.Divide(price.Body, Expression.Constant((decimal?)rate, typeof(decimal?)));
            result = Expression.Condition(codeEquals, converted, result);
        }

        return Expression.Lambda<Func<Property, decimal?>>(result, parameter);
    }

    /// <summary>
    /// "Is this listing priced within [min, max] expressed in <paramref name="selectedCode"/>?"
    /// A listing matches when its price, converted into the selected currency, lies in the range;
    /// equivalently, its own price lies in the range converted into its own currency. Evaluating
    /// per currency (one branch each) keeps every number a constant, so the database can still
    /// use the price columns directly. The field rules are the established ones: a minimum is met
    /// by any populated price field, a maximum must hold for every populated field.
    /// </summary>
    public static Expression<Func<Property, bool>>? PriceRange(
        ListingType? listingType,
        decimal? min,
        decimal? max,
        string selectedCode,
        CurrencyRates rates)
    {
        if (min is null && max is null)
            return null;

        var parameter = Expression.Parameter(typeof(Property), "p");
        Expression? any = null;

        // Currencies with a rate are converted; the selected currency always compares as-is
        // (also when it has no rate on file); any other unpriced currency never matches.
        var branches = new List<(string Code, decimal? Min, decimal? Max)>();
        var selectedKnown = rates.TryGetRate(selectedCode, out var selectedRate);

        branches.Add((selectedCode.ToUpperInvariant(), min, max));
        if (selectedKnown)
        {
            foreach (var (code, rate) in rates.UnitsPerUsd)
            {
                if (string.Equals(code, selectedCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                // amount in `code` = amount in selected * rate(code) / rate(selected)
                decimal? lo = min is null ? null : min * rate / selectedRate;
                decimal? hi = max is null ? null : max * rate / selectedRate;
                branches.Add((code, lo, hi));
            }
        }

        foreach (var (code, lo, hi) in branches)
        {
            var inCurrency = Expression.Equal(
                Expression.Property(parameter, nameof(Property.CurrencyCode)),
                Parameterised(code));
            var withinRange = Combine(RangeFor(parameter, listingType, lo, hi), inCurrency);
            any = any is null ? withinRange : Expression.OrElse(any, withinRange);
        }

        return Expression.Lambda<Func<Property, bool>>(any!, parameter);
    }

    /// <summary>
    /// A value that EF Core sends as a query parameter instead of baking into the SQL text.
    /// A literal constant would give every distinct number a visitor types its own compiled
    /// query in EF's cache; a member read on a constant holder is parameterised like a captured
    /// variable, so the cache holds one entry per query shape.
    /// </summary>
    private static Expression Parameterised<T>(T value)
        => Expression.Property(Expression.Constant(new Holder<T>(value)), nameof(Holder<T>.Value));

    private sealed class Holder<T>
    {
        public Holder(T value) => Value = value;
        public T Value { get; }
    }

    private static Expression Combine(Expression? range, Expression inCurrency)
        => range is null ? inCurrency : Expression.AndAlso(inCurrency, range);

    private static Expression? RangeFor(ParameterExpression p, ListingType? listingType, decimal? min, decimal? max)
    {
        Expression? result = null;

        if (min is { } lo)
        {
            var bound = Parameterised((decimal?)lo);
            Expression Ge(string name) => Expression.GreaterThanOrEqual(Expression.Property(p, name), bound);

            Expression minimum = listingType switch
            {
                ListingType.ForRent => Expression.OrElse(Ge(nameof(Property.ColdRent)), Ge(nameof(Property.WarmRent))),
                ListingType.ForSale => Ge(nameof(Property.PurchasePrice)),
                _ => Expression.OrElse(
                    Expression.OrElse(Ge(nameof(Property.ColdRent)), Ge(nameof(Property.WarmRent))),
                    Ge(nameof(Property.PurchasePrice)))
            };
            result = minimum;
        }

        if (max is { } hi)
        {
            var bound = Parameterised((decimal?)hi);
            Expression NullOrLe(string name) => Expression.OrElse(
                Expression.Equal(Expression.Property(p, name), Expression.Constant(null, typeof(decimal?))),
                Expression.LessThanOrEqual(Expression.Property(p, name), bound));

            Expression maximum = listingType switch
            {
                ListingType.ForRent => Expression.AndAlso(NullOrLe(nameof(Property.ColdRent)), NullOrLe(nameof(Property.WarmRent))),
                ListingType.ForSale => NullOrLe(nameof(Property.PurchasePrice)),
                _ => Expression.AndAlso(
                    Expression.AndAlso(NullOrLe(nameof(Property.PurchasePrice)), NullOrLe(nameof(Property.ColdRent))),
                    NullOrLe(nameof(Property.WarmRent)))
            };
            result = result is null ? maximum : Expression.AndAlso(result, maximum);
        }

        return result;
    }
}
