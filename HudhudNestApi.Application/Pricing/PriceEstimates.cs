using FluentValidation;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Pricing;
using MediatR;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Application.Pricing;

public interface IAreaPriceStatsReader
{
    Task<IReadOnlyList<AreaPriceStat>> GetForGovernorateAsync(int governorateId, PriceKind kind, CancellationToken ct);
    Task<CurrencyRates> LoadRatesAsync(CancellationToken ct);
}

/// <summary>Which level of data the estimate came from, most specific first.</summary>
public enum EstimateBasis
{
    DistrictAndType = 1,
    District = 2,
    GovernorateAndType = 3,
    Governorate = 4,
}

public sealed record PriceEstimateDto(
    bool Available,
    string? Reason,
    PriceKind Kind,
    string CurrencyCode,
    decimal? Low,
    decimal? Mid,
    decimal? High,
    decimal? LowPerSqm,
    decimal? MidPerSqm,
    decimal? HighPerSqm,
    int SampleCount,
    string? Basis,
    string? Confidence,
    DateTime? DataComputedAt,
    DateTime? OldestListingAt,
    DateTime? NewestListingAt);

public sealed record AreaPriceRowDto(
    int GovernorateId,
    int? DistrictId,
    int? PropertyTypeId,
    PriceKind Kind,
    string CurrencyCode,
    decimal P25PerSqm,
    decimal MedianPerSqm,
    decimal P75PerSqm,
    int SampleCount,
    string Confidence,
    DateTime ComputedAt);

/// <summary>
/// Growth engine P6: a statistical range from live asking prices, never a valuation. "Not enough data" is an
/// answer, not an error. The range is the middle half of comparable listings (P25..P75) times the area.
/// </summary>
public sealed record GetPriceEstimateQuery(
    PriceKind Kind, int GovernorateId, int? DistrictId, int? PropertyTypeId, decimal AreaSqm, string? CurrencyCode)
    : IRequest<PriceEstimateDto>;

public sealed record GetAreaPricesQuery(int GovernorateId, PriceKind Kind, string? CurrencyCode) : IRequest<IReadOnlyList<AreaPriceRowDto>>;

public sealed class GetPriceEstimateQueryValidator : AbstractValidator<GetPriceEstimateQuery>
{
    public GetPriceEstimateQueryValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.GovernorateId).GreaterThan(0);
        RuleFor(x => x.DistrictId).GreaterThan(0).When(x => x.DistrictId.HasValue);
        RuleFor(x => x.PropertyTypeId).GreaterThan(0).When(x => x.PropertyTypeId.HasValue);
        RuleFor(x => x.AreaSqm).GreaterThan(0).LessThanOrEqualTo(100_000);
        RuleFor(x => x.CurrencyCode).Must(SearchCurrencies.IsSupported).WithMessage(SearchCurrencies.UnsupportedMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode));
    }
}

public sealed class GetAreaPricesQueryValidator : AbstractValidator<GetAreaPricesQuery>
{
    public GetAreaPricesQueryValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.GovernorateId).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).Must(SearchCurrencies.IsSupported).WithMessage(SearchCurrencies.UnsupportedMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode));
    }
}

public sealed class PriceEstimateHandlers :
    IRequestHandler<GetPriceEstimateQuery, PriceEstimateDto>,
    IRequestHandler<GetAreaPricesQuery, IReadOnlyList<AreaPriceRowDto>>
{
    private readonly IAreaPriceStatsReader _reader;
    private readonly AreaPriceOptions _options;

    public PriceEstimateHandlers(IAreaPriceStatsReader reader, IOptions<AreaPriceOptions> options)
    {
        _reader = reader;
        _options = options.Value;
    }

    public async Task<PriceEstimateDto> Handle(GetPriceEstimateQuery q, CancellationToken ct)
    {
        var currency = Currency(q.CurrencyCode);
        if (q.AreaSqm < _options.MinAreaSqm || q.AreaSqm > _options.MaxAreaSqm)
            return NotAvailable(q.Kind, currency, "area_out_of_range");

        var rows = await _reader.GetForGovernorateAsync(q.GovernorateId, q.Kind, ct);
        var (row, basis) = Pick(rows, q.DistrictId, q.PropertyTypeId);
        if (row is null)
            return NotAvailable(q.Kind, currency, "not_enough_data");

        var factor = await FactorAsync(currency, ct);
        if (factor is null)
            return NotAvailable(q.Kind, currency, "currency_rate_missing");

        decimal Money(decimal perSqmUsd) => decimal.Round(perSqmUsd * q.AreaSqm * factor.Value, 0, MidpointRounding.AwayFromZero);
        decimal PerSqm(decimal perSqmUsd) => decimal.Round(perSqmUsd * factor.Value, 2, MidpointRounding.AwayFromZero);

        return new PriceEstimateDto(
            true, null, q.Kind, currency,
            Money(row.P25PerSqmUsd), Money(row.MedianPerSqmUsd), Money(row.P75PerSqmUsd),
            PerSqm(row.P25PerSqmUsd), PerSqm(row.MedianPerSqmUsd), PerSqm(row.P75PerSqmUsd),
            row.SampleCount, basis.ToString(), row.Confidence.ToString(),
            row.ComputedAt, row.OldestListingAt, row.NewestListingAt);
    }

    public async Task<IReadOnlyList<AreaPriceRowDto>> Handle(GetAreaPricesQuery q, CancellationToken ct)
    {
        var currency = Currency(q.CurrencyCode);
        var factor = await FactorAsync(currency, ct);
        if (factor is null)
            return [];
        var rows = await _reader.GetForGovernorateAsync(q.GovernorateId, q.Kind, ct);
        return rows
            .OrderBy(r => r.DistrictId.HasValue).ThenBy(r => r.DistrictId).ThenBy(r => r.PropertyTypeId)
            .Select(r => new AreaPriceRowDto(
                r.GovernorateId, r.DistrictId, r.PropertyTypeId, r.Kind, currency,
                decimal.Round(r.P25PerSqmUsd * factor.Value, 2), decimal.Round(r.MedianPerSqmUsd * factor.Value, 2),
                decimal.Round(r.P75PerSqmUsd * factor.Value, 2), r.SampleCount, r.Confidence.ToString(), r.ComputedAt))
            .ToList();
    }

    /// <summary>The most specific row that exists; rows only exist when the sample was big enough.</summary>
    public static (AreaPriceStat? Row, EstimateBasis Basis) Pick(IReadOnlyList<AreaPriceStat> rows, int? districtId, int? propertyTypeId)
    {
        AreaPriceStat? Find(int? d, int? t) => rows.FirstOrDefault(r => r.DistrictId == d && r.PropertyTypeId == t);

        if (districtId.HasValue && propertyTypeId.HasValue && Find(districtId, propertyTypeId) is { } a)
            return (a, EstimateBasis.DistrictAndType);
        if (districtId.HasValue && Find(districtId, null) is { } b)
            return (b, EstimateBasis.District);
        if (propertyTypeId.HasValue && Find(null, propertyTypeId) is { } c)
            return (c, EstimateBasis.GovernorateAndType);
        if (Find(null, null) is { } d2)
            return (d2, EstimateBasis.Governorate);
        return (null, EstimateBasis.Governorate);
    }

    private async Task<decimal?> FactorAsync(string currency, CancellationToken ct)
    {
        if (currency == "USD")
            return 1m;
        // Rates are "units per USD"; the USD row itself is not required.
        var rates = await _reader.LoadRatesAsync(ct);
        return rates.TryGetRate(currency, out var unitsPerUsd) ? unitsPerUsd : null;
    }

    private static string Currency(string? code) => string.IsNullOrWhiteSpace(code) ? "USD" : code.Trim().ToUpperInvariant();

    private static PriceEstimateDto NotAvailable(PriceKind kind, string currency, string reason) =>
        new(false, reason, kind, currency, null, null, null, null, null, null, 0, null, null, null, null, null);
}
