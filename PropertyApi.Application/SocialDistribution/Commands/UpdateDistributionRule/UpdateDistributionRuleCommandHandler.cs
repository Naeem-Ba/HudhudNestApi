using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Commands.UpdateDistributionRule;

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
