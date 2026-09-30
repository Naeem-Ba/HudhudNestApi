using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ArchiveDistributionRule;

public sealed class ArchiveDistributionRuleCommandHandler : IRequestHandler<ArchiveDistributionRuleCommand, DistributionRuleDto>
{
    private readonly IDistributionRuleRepository _rules;
    private readonly IUnitOfWork _uow;

    public ArchiveDistributionRuleCommandHandler(IDistributionRuleRepository rules, IUnitOfWork uow)
    {
        _rules = rules;
        _uow = uow;
    }

    public async Task<DistributionRuleDto> Handle(ArchiveDistributionRuleCommand request, CancellationToken ct)
    {
        var rule = await _rules.GetByIdAsync(request.RuleId, ct)
            ?? throw new NotFoundException("قاعدة التوزيع غير موجودة.");

        rule.Archive(request.ArchivedByUserId);
        _rules.Update(rule);
        await _uow.SaveChangesAsync(ct);

        return DistributionMapper.ToDto(rule);
    }
}
