using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateDistributionRule;

public sealed class CreateDistributionRuleCommandHandler : IRequestHandler<CreateDistributionRuleCommand, DistributionRuleDto>
{
    private readonly IDistributionRuleRepository _rules;
    private readonly ISocialAccountRepository _accounts;
    private readonly IUnitOfWork _uow;

    public CreateDistributionRuleCommandHandler(IDistributionRuleRepository rules, ISocialAccountRepository accounts, IUnitOfWork uow)
    {
        _rules = rules;
        _accounts = accounts;
        _uow = uow;
    }

    public async Task<DistributionRuleDto> Handle(CreateDistributionRuleCommand request, CancellationToken ct)
    {
        // The target account must exist — its live Active/Inactive status is deliberately NOT
        // required here (spec §6: a rule can be authored before its account is connected, and
        // DistributionEngine re-checks eligibility live at evaluation time every time).
        _ = await _accounts.GetByIdAsync(request.SocialAccountId, ct)
            ?? throw new NotFoundException("الحساب الاجتماعي المستهدف غير موجود.");

        var rule = DistributionRule.Create(
            request.Name,
            request.Description,
            request.ProvinceId,
            request.PropertyTypeId,
            request.TransactionType,
            request.SocialAccountId,
            request.Priority,
            request.StartAt,
            request.EndAt,
            request.CreatedByUserId);

        await _rules.AddAsync(rule, ct);
        await _uow.SaveChangesAsync(ct);

        return DistributionMapper.ToDto(rule);
    }
}
