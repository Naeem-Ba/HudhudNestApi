using MediatR;
using HudhudNestApi.Application.Reviews.DTOs;

namespace HudhudNestApi.Application.Reviews.Commands.AddReview;

public sealed record AddReviewCommand(
    Guid PropertyId,
    Guid ReviewerId,
    int Rating,
    string? Comment
) : IRequest<PropertyReviewDto>;
