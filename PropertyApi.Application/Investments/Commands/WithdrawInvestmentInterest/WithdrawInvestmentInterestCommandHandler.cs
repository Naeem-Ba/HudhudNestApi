using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.WithdrawInvestmentInterest;

public sealed class WithdrawInvestmentInterestCommandHandler
    : IRequestHandler<WithdrawInvestmentInterestCommand, InvestmentInterestMutationResult>
{
    private readonly IInvestmentInterestRepository _interests;
    private readonly IUnitOfWork _uow;

    public WithdrawInvestmentInterestCommandHandler(IInvestmentInterestRepository interests, IUnitOfWork uow)
    {
        _interests = interests;
        _uow = uow;
    }

    public async Task<InvestmentInterestMutationResult> Handle(
        WithdrawInvestmentInterestCommand request,
        CancellationToken ct)
    {
        var interest = await _interests.GetAsync(request.UserId, request.InvestmentProjectId, ct);
        if (interest is null || interest.Status != InvestmentInterestStatus.Active)
            return InvestmentInterestMutationResult.NotFound();

        interest.Withdraw();
        await _uow.SaveChangesAsync(ct);

        return InvestmentInterestMutationResult.Success(
            new InvestmentInterestDto(interest.Id, interest.InvestmentProjectId, string.Empty, interest.Status, interest.CreatedAt),
            "Interest withdrawn.");
    }
}
