using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.QueueSocialPublication;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.SocialDistribution.Services;

namespace PropertyApi.Application.SocialDistribution.Services;

/// <summary>
/// The Phase 4 orchestrator (spec §8): PropertyPublished/manual trigger → load active rules
/// (DB-filtered) → evaluate → group by SocialAccountId → pick the winning rule per account →
/// skip ineligible/duplicate targets → create + queue one SocialPublication per remaining winner
/// → record the whole pass as one DistributionRun.
///
/// Deliberately does NOT duplicate SocialPublication-creation/UTM/content-generation logic:
/// every winning target is dispatched through the exact same
/// <see cref="CreateSocialPublicationCommand"/> → <see cref="QueueSocialPublicationCommand"/>
/// pipeline the manual "create one publication by hand" API endpoint uses (via <see cref="ISender"/>),
/// so there is exactly one place that enforces "property must be public", "account must be
/// active", builds the UTM-attributed TargetUrl, and generates default content from the
/// property — Phase 3's CreateSocialPublicationCommandHandler. This engine only decides WHICH
/// (rule, account) pairs are worth calling that pipeline for.
/// </summary>
public sealed class DistributionEngine : IDistributionEngine
{
    private readonly IPropertyRepository _properties;
    private readonly IDistributionRuleRepository _rules;
    private readonly IDistributionRunRepository _runs;
    private readonly ISocialAccountRepository _accounts;
    private readonly ISocialChannelRepository _channels;
    private readonly ISocialPublicationRepository _publications;
    private readonly ISender _sender;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DistributionEngine> _logger;

    public DistributionEngine(
        IPropertyRepository properties,
        IDistributionRuleRepository rules,
        IDistributionRunRepository runs,
        ISocialAccountRepository accounts,
        ISocialChannelRepository channels,
        ISocialPublicationRepository publications,
        ISender sender,
        IUnitOfWork uow,
        ILogger<DistributionEngine> logger)
    {
        _properties = properties;
        _rules = rules;
        _runs = runs;
        _accounts = accounts;
        _channels = channels;
        _publications = publications;
        _sender = sender;
        _uow = uow;
        _logger = logger;
    }

    public async Task<DistributionRunDto> RunAsync(
        Guid propertyId, DistributionRunTriggerType triggerType, Guid? triggeredByUserId, CancellationToken ct = default)
    {
        var property = await _properties.GetPublishedByIdWithDetailsAsync(propertyId, ct)
            ?? throw new ConflictException("لا يمكن توزيع عقار غير منشور أو غير عام أو غير موجود.");

        var run = DistributionRun.Create(propertyId, triggerType, triggeredByUserId);
        await _runs.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct); // durable "a run started" record before anything else can fail

        run.MarkEvaluating(DateTime.UtcNow);
        _runs.Update(run);
        await _uow.SaveChangesAsync(ct);

        var candidate = new DistributionCandidateProperty(property.Id, property.GovernorateId, property.PropertyTypeId, property.ListingType);
        var utcNow = DateTime.UtcNow;

        var candidateRules = await _rules.GetActiveCandidatesAsync(
            candidate.GovernorateId, candidate.PropertyTypeId, candidate.ListingType, utcNow, ct);

        // The DB query above is a performance pre-filter (spec §23) — DistributionRuleEvaluator
        // is still the single source of truth for whether a rule actually matches.
        var matchingRules = candidateRules.Where(rule => DistributionRuleEvaluator.Matches(rule, candidate, utcNow)).ToList();

        if (matchingRules.Count == 0)
        {
            run.Complete(matchedRuleCount: 0, publicationsCreatedCount: 0, skippedCount: 0, DateTime.UtcNow, resultReason: "NoMatchingRules");
            _runs.Update(run);
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("لا توجد قواعد توزيع مطابقة للعقار {PropertyId}.", propertyId);
            return DistributionMapper.ToDto(run);
        }

        // Spec §11's default policy: group by target account, one winning rule (and therefore
        // one SocialPublication) per account — never two publications on the same account from
        // one run.
        var winnersByAccount = matchingRules
            .GroupBy(rule => rule.SocialAccountId)
            .Select(group => (AccountId: group.Key, Rule: DistributionRuleEvaluator.SelectWinner(group)!))
            .ToList();

        var createdCount = 0;
        var skippedCount = 0;

        foreach (var (accountId, winningRule) in winnersByAccount)
        {
            var skipReason = await TryGetSkipReasonAsync(propertyId, accountId, ct);
            if (skipReason is not null)
            {
                skippedCount++;
                _logger.LogInformation(
                    "تخطي الحساب {SocialAccountId} للعقار {PropertyId} (القاعدة {RuleId}): {Reason}",
                    accountId, propertyId, winningRule.Id, skipReason);
                continue;
            }

            try
            {
                var created = await _sender.Send(
                    new CreateSocialPublicationCommand(
                        propertyId,
                        accountId,
                        // System-triggered runs (PropertyPublished) have no acting admin user —
                        // Guid.Empty is this codebase's existing "system, not a person" marker
                        // convention for audit columns that are non-nullable at the call site.
                        triggeredByUserId ?? Guid.Empty,
                        Title: null,
                        Body: null,
                        ImageUrl: null,
                        Hashtags: null,
                        Language: "ar",
                        DistributionRuleId: winningRule.Id,
                        DistributionRunId: run.Id),
                    ct);

                await _sender.Send(
                    new QueueSocialPublicationCommand(created.Id, triggeredByUserId ?? Guid.Empty, ScheduledAt: null),
                    ct);

                createdCount++;
            }
            catch (Exception ex) when (ex is DomainException or ConflictException or NotFoundException)
            {
                // One ineligible/invalid target must never abort the whole run (spec §20:
                // "فشل إنشاء Publication بعد إنشاء بعضها ... لا تترك الحالة غامضة") — the run's
                // own counters record exactly how many succeeded vs. were skipped.
                skippedCount++;
                _logger.LogWarning(ex, "تعذّر إنشاء منشور توزيع للحساب {SocialAccountId} والعقار {PropertyId}.", accountId, propertyId);
            }
        }

        run.Complete(
            matchedRuleCount: matchingRules.Select(r => r.Id).Distinct().Count(),
            publicationsCreatedCount: createdCount,
            skippedCount: skippedCount,
            DateTime.UtcNow,
            resultReason: createdCount == 0 && skippedCount > 0 ? "AllTargetsIneligible" : null);

        _runs.Update(run);
        await _uow.SaveChangesAsync(ct);

        return DistributionMapper.ToDto(run);
    }

    public async Task<DistributionPreviewDto> PreviewAsync(Guid propertyId, CancellationToken ct = default)
    {
        var property = await _properties.GetPublishedByIdWithDetailsAsync(propertyId, ct)
            ?? throw new ConflictException("لا يمكن معاينة توزيع عقار غير منشور أو غير عام أو غير موجود.");

        var candidate = new DistributionCandidateProperty(property.Id, property.GovernorateId, property.PropertyTypeId, property.ListingType);
        var utcNow = DateTime.UtcNow;

        var candidateRules = await _rules.GetActiveCandidatesAsync(
            candidate.GovernorateId, candidate.PropertyTypeId, candidate.ListingType, utcNow, ct);

        var matchingRules = candidateRules.Where(rule => DistributionRuleEvaluator.Matches(rule, candidate, utcNow)).ToList();

        var targets = new List<DistributionPreviewTargetDto>();

        foreach (var group in matchingRules.GroupBy(rule => rule.SocialAccountId))
        {
            var winningRule = DistributionRuleEvaluator.SelectWinner(group)!;
            var account = await _accounts.GetByIdAsync(group.Key, ct);
            var skipReason = await TryGetSkipReasonAsync(propertyId, group.Key, ct);

            targets.Add(new DistributionPreviewTargetDto(
                group.Key,
                account?.DisplayName ?? "(حساب غير موجود)",
                winningRule.Id,
                winningRule.Name,
                WouldPublish: skipReason is null,
                SkipReason: skipReason));
        }

        return new DistributionPreviewDto(propertyId, matchingRules.Select(r => r.Id).Distinct().Count(), targets);
    }

    /// <summary>
    /// The live eligibility gate every winning (rule, account) pair must clear before a
    /// SocialPublication is ever created (spec §10: "AND SocialAccount is Active AND
    /// SocialChannel is Active" + §12 idempotency). Returns a short, human-readable reason when
    /// the target must be skipped, or null when it is eligible.
    /// </summary>
    private async Task<string?> TryGetSkipReasonAsync(Guid propertyId, Guid socialAccountId, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(socialAccountId, ct);
        if (account is null)
            return "الحساب الاجتماعي غير موجود.";

        if (!account.CanPublish())
            return $"الحساب الاجتماعي غير فعّال (الحالة الحالية: {account.Status}).";

        var channel = await _channels.GetByIdAsync(account.SocialChannelId, ct);
        if (channel is null || !channel.CanBackNewAccounts())
            return "القناة الاجتماعية لهذا الحساب غير فعّالة.";

        if (await _publications.ExistsActiveForPropertyAndAccountAsync(propertyId, socialAccountId, ct))
            return "يوجد بالفعل منشور توزيع نشط لهذا العقار على هذا الحساب.";

        return null;
    }
}
