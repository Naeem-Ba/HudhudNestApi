using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Commands.WithdrawInvestmentInterest;

public sealed record WithdrawInvestmentInterestCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentInterestMutationResult>;
