using MediatR;
using PropertyApi.Application.Reviews.DTOs;

namespace PropertyApi.Application.Reviews.Commands.AddReview;

public sealed record AddReviewCommand(
    Guid    PropertyId,
    Guid    ReviewerId,
    int     Rating,
    string? Comment
) : IRequest<PropertyReviewDto>;