using FluentValidation;
using MediatR;

namespace HudhudNestApi.Application.Growth;

/// <summary>Counts for one UTC day.</summary>
public sealed record GrowthDayDto(
    DateOnly Day, int NewAccounts, int ListingsPublished, int SearchRequests, int SavedSearches,
    int Favorites, int Contacts, int ActiveUsers);

/// <summary>Accounts that signed up with the same UTM tags in the period, and what they did in their first 30 days.</summary>
public sealed record GrowthCampaignDto(
    string? Source, string? Medium, string? Campaign, int Registered, int Activated, int Published, int Contacted);

/// <summary>Of the accounts created in the period that are at least N days old, how many came back on day N or later.</summary>
public sealed record RetentionDto(int Days, int Cohort, int Retained);

public sealed record GrowthTotalsDto(
    int NewAccounts, int ListingsPublished, int SearchRequests, int SavedSearches, int Favorites,
    int Contacts, int Proposals, int ReferralsQualified, int ActiveUsers);

/// <summary>Sign-up funnel for accounts created in the period (first 30 days after sign-up).</summary>
public sealed record GrowthFunnelDto(int Registered, int Activated, int Published, int Contacted);

public sealed record GrowthOverviewDto(
    DateTime FromUtc,
    DateTime ToUtc,
    GrowthTotalsDto Totals,
    GrowthFunnelDto Funnel,
    IReadOnlyList<GrowthDayDto> Days,
    IReadOnlyList<GrowthCampaignDto> Campaigns,
    IReadOnlyList<RetentionDto> Retention,
    DateTime? ActivityDataSince,
    int UntaggedRegistrations);

public interface IGrowthDashboardReader
{
    Task<GrowthOverviewDto> GetOverviewAsync(DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Growth engine P10: admin growth overview. Every number comes from <c>GrowthEvents</c> or <c>UserAccounts</c>;
/// definitions are in docs/growth-engine/GROWTH-METRICS.md. Window: at most 186 days.
/// </summary>
public sealed record GetGrowthOverviewQuery(DateTime? FromUtc, DateTime? ToUtc) : IRequest<GrowthOverviewDto>;

public sealed class GetGrowthOverviewQueryValidator : AbstractValidator<GetGrowthOverviewQuery>
{
    public const int MaxDays = 186;

    public GetGrowthOverviewQueryValidator()
    {
        RuleFor(x => x).Must(x => !x.FromUtc.HasValue || !x.ToUtc.HasValue || x.FromUtc < x.ToUtc)
            .WithMessage("fromUtc must be before toUtc.");
        RuleFor(x => x).Must(x => !x.FromUtc.HasValue || !x.ToUtc.HasValue || (x.ToUtc.Value - x.FromUtc.Value).TotalDays <= MaxDays)
            .WithMessage($"The period can be at most {MaxDays} days.");
    }
}

public sealed class GetGrowthOverviewQueryHandler : IRequestHandler<GetGrowthOverviewQuery, GrowthOverviewDto>
{
    private readonly IGrowthDashboardReader _reader;
    private readonly TimeProvider _time;

    public GetGrowthOverviewQueryHandler(IGrowthDashboardReader reader, TimeProvider? time = null)
    {
        _reader = reader;
        _time = time ?? TimeProvider.System;
    }

    public Task<GrowthOverviewDto> Handle(GetGrowthOverviewQuery q, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var (from, to) = Window(q.FromUtc, q.ToUtc, now);
        return _reader.GetOverviewAsync(from, to, now, ct);
    }

    /// <summary>Default: the last 30 full UTC days plus today. Only one end given: 30 days from it. Capped at 186 days.</summary>
    public static (DateTime From, DateTime To) Window(DateTime? fromUtc, DateTime? toUtc, DateTime nowUtc)
    {
        var to = toUtc ?? (fromUtc?.AddDays(30) ?? nowUtc.Date.AddDays(1));
        if (to > nowUtc.Date.AddDays(1))
            to = nowUtc.Date.AddDays(1);
        var from = fromUtc ?? to.AddDays(-30);
        if ((to - from).TotalDays > GetGrowthOverviewQueryValidator.MaxDays)
            from = to.AddDays(-GetGrowthOverviewQueryValidator.MaxDays);
        return (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc));
    }
}
