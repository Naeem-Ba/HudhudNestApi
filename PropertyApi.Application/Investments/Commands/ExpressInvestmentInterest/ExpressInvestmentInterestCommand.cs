using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Commands.ExpressInvestmentInterest;

/// <summary>
/// "أرغب بالاستثمار" — records ONLY that the user is interested. Never creates a payment,
/// commitment, or investment (Phase 1 spec §30).
/// </summary>
public sealed record ExpressInvestmentInterestCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentInterestMutationResult>;
