using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Commands.SetInvestmentProjectFinancials;

public sealed class SetInvestmentProjectFinancialsCommandHandler
    : IRequestHandler<SetInvestmentProjectFinancialsCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentProjectFinancialsRepository _financials;
    private readonly IUnitOfWork _uow;

    public SetInvestmentProjectFinancialsCommandHandler(
        IInvestmentProjectRepository projects,
        IInvestmentProjectFinancialsRepository financials,
        IUnitOfWork uow)
    {
        _projects = projects;
        _financials = financials;
        _uow = uow;
    }

    public async Task Handle(SetInvestmentProjectFinancialsCommand request, CancellationToken ct)
    {
        var projectExists = await _projects.GetByIdAsync(request.InvestmentProjectId, ct) is not null;
        if (!projectExists)
            throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        var financials = await _financials.GetByProjectIdAsync(request.InvestmentProjectId, ct);
        var isNew = financials is null;
        financials ??= InvestmentProjectFinancials.Create(request.InvestmentProjectId);

        financials.UpdateCosts(
            request.PurchasePrice,
            request.RenovationCost,
            request.ConstructionCost,
            request.Taxes,
            request.NotaryCost,
            request.BrokerCost,
            request.FinancingCost,
            request.OperatingCost,
            request.ContingencyReserve);

        financials.UpdateRevenueProjections(request.ExpectedRevenue, request.ExpectedProfit);

        if (isNew)
            _financials.Add(financials);

        await _uow.SaveChangesAsync(ct);
    }
}
