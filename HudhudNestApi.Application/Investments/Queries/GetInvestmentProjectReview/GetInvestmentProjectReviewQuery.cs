using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectReview;

public sealed record GetInvestmentProjectReviewQuery(Guid Id) : IRequest<InvestmentProjectReviewDto?>;
