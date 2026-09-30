using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Commands.ExpressInvestmentInterest;

/// <summary>
/// "أرغب بالاستثمار" — records ONLY that the user is interested. Never creates a payment,
/// commitment, or investment (Phase 1 spec §30).
/// </summary>
public sealed record ExpressInvestmentInterestCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentInterestMutationResult>;
