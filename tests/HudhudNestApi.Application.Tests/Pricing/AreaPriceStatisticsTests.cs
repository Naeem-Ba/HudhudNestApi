using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Application.Pricing;
using HudhudNestApi.Domain.Pricing;
using Microsoft.Extensions.Options;
using Xunit;

namespace HudhudNestApi.Application.Tests.Pricing;

public sealed class AreaPriceStatisticsTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly AreaPriceOptions Options = new();

    private static List<PriceSample> Samples(params decimal[] perSqm) =>
        perSqm.Select((v, i) => new PriceSample(Guid.NewGuid(), 1, 100m, v * 100m, T0.AddDays(i))).ToList();

    [Fact]
    public void Quantile_uses_linear_interpolation()
    {
        decimal[] v = [1, 2, 3, 4];
        Assert.Equal(1.75m, AreaPriceStatistics.Quantile(v, 0.25m));
        Assert.Equal(2.5m, AreaPriceStatistics.Quantile(v, 0.5m));
        Assert.Equal(3.25m, AreaPriceStatistics.Quantile(v, 0.75m));
    }

    [Fact]
    public void Too_few_listings_give_no_answer_not_zero() =>
        Assert.Null(AreaPriceStatistics.Summarize(Samples(10, 11, 12, 13, 14, 15, 16), Options));

    [Fact]
    public void Quartiles_and_dates_come_from_the_kept_listings()
    {
        var s = AreaPriceStatistics.Summarize(Samples(10, 11, 12, 13, 14, 15, 16, 17), Options)!;
        Assert.Equal(8, s.SampleCount);
        Assert.Equal(0, s.ExcludedCount);
        Assert.Equal(11.75m, s.P25);
        Assert.Equal(13.5m, s.Median);
        Assert.Equal(15.25m, s.P75);
        Assert.Equal(EstimateConfidence.Low, s.Confidence);
        Assert.Equal(T0, s.Oldest);
        Assert.Equal(T0.AddDays(7), s.Newest);
    }

    [Fact]
    public void An_extreme_price_is_dropped_as_an_outlier()
    {
        var s = AreaPriceStatistics.Summarize(Samples(10, 11, 12, 13, 14, 15, 16, 17, 900), Options)!;
        Assert.Equal(8, s.SampleCount);
        Assert.Equal(1, s.ExcludedCount);
        Assert.True(s.P75 < 20);
    }

    [Fact]
    public void The_same_flat_posted_twice_counts_once()
    {
        var owner = Guid.NewGuid();
        var list = Samples(10, 11, 12, 13, 14, 15, 16);
        list.Add(new PriceSample(owner, 1, 100m, 1500m, T0));
        list.Add(new PriceSample(owner, 1, 100.2m, 1500.4m, T0.AddDays(3)));
        var s = AreaPriceStatistics.Summarize(list, Options)!;
        Assert.Equal(8, s.SampleCount);
        Assert.Equal(1, s.ExcludedCount);
    }

    [Fact]
    public void Sizes_outside_the_range_are_not_used()
    {
        var list = Samples(10, 11, 12, 13, 14, 15, 16);
        list.Add(new PriceSample(Guid.NewGuid(), 1, 5m, 70m, T0)); // 5 m²: below MinAreaSqm
        Assert.Null(AreaPriceStatistics.Summarize(list, Options));
    }

    [Fact]
    public void One_owner_alone_never_sets_an_area_price()
    {
        var owner = Guid.NewGuid();
        var list = Enumerable.Range(0, 20).Select(i => new PriceSample(owner, 1, 80m + i, (80m + i) * 12m, T0)).ToList();
        Assert.Null(AreaPriceStatistics.Summarize(list, Options));
    }

    [Fact]
    public void Confidence_grows_with_the_sample_and_drops_when_prices_are_spread()
    {
        var medium = AreaPriceStatistics.Summarize(Samples(Enumerable.Range(0, 20).Select(i => 100m + i).ToArray()), Options)!;
        Assert.Equal(EstimateConfidence.Medium, medium.Confidence);

        var high = AreaPriceStatistics.Summarize(Samples(Enumerable.Range(0, 45).Select(i => 100m + i).ToArray()), Options)!;
        Assert.Equal(EstimateConfidence.High, high.Confidence);

        // Spread wider than 60 % of the median: one level lower.
        var spread = AreaPriceStatistics.Summarize(Samples(Enumerable.Range(0, 45).Select(i => 20m + i * 4m).ToArray()), Options)!;
        Assert.Equal(EstimateConfidence.Medium, spread.Confidence);
    }

    private static AreaPriceStat Row(int? district, int? type, decimal median = 1000m) =>
        AreaPriceStat.Create(1, district, type, PriceKind.Sale, 10, 0, median * 0.8m, median, median * 1.2m,
            EstimateConfidence.Low, T0, T0, T0);

    [Fact]
    public void Pick_prefers_the_most_specific_row_and_says_which_it_used()
    {
        var rows = new[] { Row(null, null), Row(null, 2), Row(5, null), Row(5, 2) };
        Assert.Equal(EstimateBasis.DistrictAndType, PriceEstimateHandlers.Pick(rows, 5, 2).Basis);
        Assert.Equal(EstimateBasis.District, PriceEstimateHandlers.Pick(rows, 5, 3).Basis);
        Assert.Equal(EstimateBasis.GovernorateAndType, PriceEstimateHandlers.Pick(rows, 6, 2).Basis);
        Assert.Equal(EstimateBasis.Governorate, PriceEstimateHandlers.Pick(rows, 6, 3).Basis);
        Assert.Null(PriceEstimateHandlers.Pick([], 5, 2).Row);
    }

    private sealed class FakeReader(IReadOnlyList<AreaPriceStat> rows, CurrencyRates rates) : IAreaPriceStatsReader
    {
        public Task<IReadOnlyList<AreaPriceStat>> GetForGovernorateAsync(int governorateId, PriceKind kind, CancellationToken ct) =>
            Task.FromResult(rows);
        public Task<CurrencyRates> LoadRatesAsync(CancellationToken ct) => Task.FromResult(rates);
    }

    private static PriceEstimateHandlers Handler(IReadOnlyList<AreaPriceStat> rows, CurrencyRates? rates = null) =>
        new(new FakeReader(rows, rates ?? CurrencyRates.Empty), Microsoft.Extensions.Options.Options.Create(new AreaPriceOptions()));

    [Fact]
    public async Task Estimate_is_the_middle_half_times_the_area()
    {
        var dto = await Handler([Row(null, null, 1000m)]).Handle(
            new GetPriceEstimateQuery(PriceKind.Sale, 1, null, null, 100m, null), default);
        Assert.True(dto.Available);
        Assert.Equal("USD", dto.CurrencyCode);
        Assert.Equal(80_000m, dto.Low);
        Assert.Equal(100_000m, dto.Mid);
        Assert.Equal(120_000m, dto.High);
        Assert.Equal(10, dto.SampleCount);
        Assert.Equal("Governorate", dto.Basis);
        Assert.Equal("Low", dto.Confidence);
    }

    [Fact]
    public async Task Estimate_converts_currency_and_refuses_without_a_rate()
    {
        var eur = await Handler([Row(null, null)], new CurrencyRates(new Dictionary<string, decimal> { ["EUR"] = 0.9m }))
            .Handle(new GetPriceEstimateQuery(PriceKind.Sale, 1, null, null, 100m, "eur"), default);
        Assert.Equal(90_000m, eur.Mid);
        Assert.Equal("EUR", eur.CurrencyCode);

        var none = await Handler([Row(null, null)]).Handle(new GetPriceEstimateQuery(PriceKind.Sale, 1, null, null, 100m, "SYP"), default);
        Assert.False(none.Available);
        Assert.Equal("currency_rate_missing", none.Reason);
        Assert.Null(none.Mid);
    }

    [Theory]
    [InlineData(5, "area_out_of_range")]
    [InlineData(100, "not_enough_data")]
    public async Task Estimate_says_why_it_has_no_answer(int area, string reason)
    {
        var dto = await Handler([]).Handle(new GetPriceEstimateQuery(PriceKind.Rent, 1, 3, null, area, null), default);
        Assert.False(dto.Available);
        Assert.Equal(reason, dto.Reason);
        Assert.Equal(0, dto.SampleCount);
    }
}
