using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class SurveyResponseRepository : ISurveyResponseRepository
{
    private readonly AppDbContext _db;

    public SurveyResponseRepository(AppDbContext db)
        => _db = db;

    public void Add(SurveyResponse response)
    {
        _db.SurveyResponses.Add(response);
    }

    public async Task<SurveyResponsesPageDto> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.SurveyResponses.AsNoTracking().OrderByDescending(s => s.CreatedAt);

        var total = await query.CountAsync(ct);

        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SurveyResponseDto(
                s.Id,
                s.LeadId,
                s.WillingnessToPay == null ? null : s.WillingnessToPay.ToString(),
                s.PreferredPaymentModel == null ? null : s.PreferredPaymentModel.ToString(),
                s.ExpectedMonthlyPriceUsd,
                s.ExpectedPerListingPriceUsd,
                s.AcceptableCommissionPercent,
                s.MostImportantFeature,
                s.BiggestProblem,
                s.SubscriptionBlocker,
                s.WantsTrialBeforePaying,
                s.TeamSize,
                s.PropertyCount,
                s.UsesSimilarToolCurrently,
                s.SimilarToolName,
                s.Source,
                s.CreatedAt))
            .ToListAsync(ct);

        return new SurveyResponsesPageDto(total, page, pageSize, data);
    }

    public async Task<SurveyStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var query = _db.SurveyResponses.AsNoTracking();

        var total = await query.CountAsync(ct);

        var willingnessBreakdown = await query
            .Where(s => s.WillingnessToPay != null)
            .GroupBy(s => s.WillingnessToPay!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var paymentModelBreakdown = await query
            .Where(s => s.PreferredPaymentModel != null)
            .GroupBy(s => s.PreferredPaymentModel!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Pulled client-side rather than translated Average() — small, bounded volume for a
        // pre-launch survey, and this sidesteps provider-specific nullable-decimal Average()
        // translation quirks entirely (see OfferRepository's doc comment for the analogous
        // "InMemory provider support" caution with ExecuteUpdateAsync).
        var monthlyPrices = await query
            .Where(s => s.ExpectedMonthlyPriceUsd != null)
            .Select(s => s.ExpectedMonthlyPriceUsd!.Value)
            .ToListAsync(ct);

        var perListingPrices = await query
            .Where(s => s.ExpectedPerListingPriceUsd != null)
            .Select(s => s.ExpectedPerListingPriceUsd!.Value)
            .ToListAsync(ct);

        var commissionPercents = await query
            .Where(s => s.AcceptableCommissionPercent != null)
            .Select(s => s.AcceptableCommissionPercent!.Value)
            .ToListAsync(ct);

        var trialWantedCount = await query.CountAsync(s => s.WantsTrialBeforePaying == true, ct);
        var usesSimilarToolCount = await query.CountAsync(s => s.UsesSimilarToolCurrently == true, ct);

        return new SurveyStatsDto(
            total,
            willingnessBreakdown.ToDictionary(x => x.Key.ToString(), x => x.Count),
            paymentModelBreakdown.ToDictionary(x => x.Key.ToString(), x => x.Count),
            monthlyPrices.Count > 0 ? monthlyPrices.Average() : null,
            perListingPrices.Count > 0 ? perListingPrices.Average() : null,
            commissionPercents.Count > 0 ? commissionPercents.Average() : null,
            trialWantedCount,
            usesSimilarToolCount);
    }
}
