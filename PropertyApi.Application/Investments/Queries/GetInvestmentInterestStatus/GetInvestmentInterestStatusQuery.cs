using MediatR;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentInterestStatus;

/// <summary>Null status means the caller has never expressed interest in this project — lets
/// the "أرغب بالاستثمار" button on a details page render its correct state without listing
/// every interest the user has.</summary>
public sealed record GetInvestmentInterestStatusQuery(Guid UserId, Guid InvestmentProjectId)
    : IRequest<InvestmentInterestStatus?>;
