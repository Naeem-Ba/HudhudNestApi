using MediatR;
using HudhudNestApi.Application.Services.DTOs;

namespace HudhudNestApi.Application.Services.Commands.AddServiceReview;

public sealed record AddServiceReviewCommand(
    Guid ServiceRequestId,
    Guid ReviewerId,
    int Rating,
    string? Comment) : IRequest<ServiceReviewDto>;
