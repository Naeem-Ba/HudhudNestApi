using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectReview;

public sealed record GetInvestmentProjectReviewQuery(Guid Id) : IRequest<InvestmentProjectReviewDto?>;
