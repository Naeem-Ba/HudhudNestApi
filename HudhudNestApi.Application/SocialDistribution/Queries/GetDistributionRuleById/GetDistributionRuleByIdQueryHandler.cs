using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetDistributionRuleById;

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
