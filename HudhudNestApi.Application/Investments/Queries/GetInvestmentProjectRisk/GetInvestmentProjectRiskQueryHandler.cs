using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectRisk;

public sealed class GetInvestmentProjectRiskQueryHandler
    : IRequestHandler<GetInvestmentProjectRiskQuery, InvestmentRiskDto?>
{
    private readonly IInvestmentRiskAssessmentRepository _risk;

    public GetInvestmentProjectRiskQueryHandler(IInvestmentRiskAssessmentRepository risk) => _risk = risk;

    public Task<InvestmentRiskDto?> Handle(GetInvestmentProjectRiskQuery request, CancellationToken ct) =>
        _risk.GetDtoAsync(request.InvestmentProjectId, ct);
}
