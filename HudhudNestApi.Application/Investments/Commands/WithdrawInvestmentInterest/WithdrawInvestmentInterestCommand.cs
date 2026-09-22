using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Commands.WithdrawInvestmentInterest;

public sealed record WithdrawInvestmentInterestCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentInterestMutationResult>;
