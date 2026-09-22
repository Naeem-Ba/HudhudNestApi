using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Commands.UpdateDistributionRule;

public sealed class UpdateDistributionRuleCommandHandler : IRequestHandler<UpdateDistributionRuleCommand, DistributionRuleDto>
{
    private readonly IDistributionRuleRepository _rules;
    private readonly IUnitOfWork _uow;

    public UpdateDistributionRuleCommandHandler(IDistributionRuleRepository rules, IUnitOfWork uow)
    {
        _rules = rules;
        _uow = uow;
    }

    public async Task<DistributionRuleDto> Handle(UpdateDistributionRuleCommand request, CancellationToken ct)
    {
        var rule = await _rules.GetByIdAsync(request.RuleId, ct)
            ?? throw new NotFoundException("قاعدة التوزيع غير موجودة.");

        rule.Update(
            request.Name,
            request.Description,
            request.ProvinceId,
            request.PropertyTypeId,
            request.TransactionType,
            request.Priority,
            request.StartAt,
            request.EndAt,
            request.UpdatedByUserId);

        _rules.Update(rule);
        await _uow.SaveChangesAsync(ct);

        return DistributionMapper.ToDto(rule);
    }
}
