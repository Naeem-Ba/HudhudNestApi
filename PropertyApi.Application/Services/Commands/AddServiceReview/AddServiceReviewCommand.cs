using MediatR;
using PropertyApi.Application.Services.DTOs;

namespace PropertyApi.Application.Services.Commands.AddServiceReview;

public sealed record AddServiceReviewCommand(
    Guid ServiceRequestId,
    Guid ReviewerId,
    int Rating,
    string? Comment) : IRequest<ServiceReviewDto>;
