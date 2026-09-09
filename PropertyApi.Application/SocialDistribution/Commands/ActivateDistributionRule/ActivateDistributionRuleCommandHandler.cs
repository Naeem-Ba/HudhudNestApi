using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Commands.ActivateDistributionRule;

public sealed class ActivateDistributionRuleCommandHandler : IRequestHandler<ActivateDistributionRuleCommand, DistributionRuleDto>
{
    private readonly IDistributionRuleRepository _rules;
    private readonly IUnitOfWork _uow;

    public ActivateDistributionRuleCommandHandler(IDistributionRuleRepository rules, IUnitOfWork uow)
    {
        _rules = rules;
        _uow = uow;
    }

    public async Task<DistributionRuleDto> Handle(ActivateDistributionRuleCommand request, CancellationToken ct)
    {
        var rule = await _rules.GetByIdAsync(request.RuleId, ct)
            ?? throw new NotFoundException("قاعدة التوزيع غير موجودة.");

        rule.Activate();
        _rules.Update(rule);
        await _uow.SaveChangesAsync(ct);

        return DistributionMapper.ToDto(rule);
    }
}
