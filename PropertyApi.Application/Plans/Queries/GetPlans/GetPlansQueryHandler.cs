using MediatR;
using PropertyApi.Application.Plans.DTOs;
using PropertyApi.Application.Plans.Interfaces;
using PropertyApi.Domain.Plans.Entities;

namespace PropertyApi.Application.Plans.Queries.GetPlans;

public sealed class GetPlansQueryHandler
    : IRequestHandler<GetPlansQuery, IReadOnlyList<PlanDto>>
{
    private readonly IPlanRepository _plans;

    public GetPlansQueryHandler(IPlanRepository plans)
    {
        _plans = plans;
    }

    public async Task<IReadOnlyList<PlanDto>> Handle(
        GetPlansQuery request,
        CancellationToken cancellationToken)
    {
        var plans = await _plans.GetActiveAsync(cancellationToken);

        return plans.Select(ToDto).ToList().AsReadOnly();
    }

    private static PlanDto ToDto(Plan plan) => new()
    {
        Tier = plan.Tier,
        NameKey = plan.NameKey,
        TaglineKey = plan.TaglineKey,
        PriceKind = plan.PriceKind,
        PriceUsd = plan.PriceUsd,
        FeatureKeys = plan.FeatureKeys,
        NoteKey = plan.NoteKey,
        IsRecommended = plan.IsRecommended,
        CtaKey = plan.CtaKey,
        ListingLimit = plan.ListingLimit
    };
}
