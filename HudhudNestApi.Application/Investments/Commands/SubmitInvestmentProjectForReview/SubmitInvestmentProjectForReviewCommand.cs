using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.SubmitInvestmentProjectForReview;

public sealed record SubmitInvestmentProjectForReviewCommand(Guid Id) : IRequest;
