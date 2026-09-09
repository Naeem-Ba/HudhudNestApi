using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.GetDistributionRuleById;

public sealed class GetDistributionRuleByIdQueryHandler : IRequestHandler<GetDistributionRuleByIdQuery, DistributionRuleDto>
{
    private readonly IDistributionRuleRepository _rules;

    public GetDistributionRuleByIdQueryHandler(IDistributionRuleRepository rules) => _rules = rules;

    public async Task<DistributionRuleDto> Handle(GetDistributionRuleByIdQuery request, CancellationToken ct)
    {
        var rule = await _rules.GetByIdAsync(request.RuleId, ct)
            ?? throw new NotFoundException("قاعدة التوزيع غير موجودة.");

        return DistributionMapper.ToDto(rule);
    }
}
