using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DeactivateDistributionRule;

public sealed class DeactivateDistributionRuleCommandHandler : IRequestHandler<DeactivateDistributionRuleCommand, DistributionRuleDto>
{
    private readonly IDistributionRuleRepository _rules;
    private readonly IUnitOfWork _uow;

    public DeactivateDistributionRuleCommandHandler(IDistributionRuleRepository rules, IUnitOfWork uow)
    {
        _rules = rules;
        _uow = uow;
    }

    public async Task<DistributionRuleDto> Handle(DeactivateDistributionRuleCommand request, CancellationToken ct)
    {
        var rule = await _rules.GetByIdAsync(request.RuleId, ct)
            ?? throw new NotFoundException("قاعدة التوزيع غير موجودة.");

        rule.Deactivate();
        _rules.Update(rule);
        await _uow.SaveChangesAsync(ct);

        return DistributionMapper.ToDto(rule);
    }
}
