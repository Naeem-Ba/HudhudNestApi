using MediatR;

namespace PropertyApi.Application.Investments.Commands.SubmitInvestmentProjectForReview;

public sealed record SubmitInvestmentProjectForReviewCommand(Guid Id) : IRequest;
