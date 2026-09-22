using MediatR;
using HudhudNestApi.Application.Services.DTOs;

namespace HudhudNestApi.Application.Services.Commands.CreateServiceRequest;

public sealed record CreateServiceRequestCommand(
    Guid PropertyId,
    Guid RequesterId,
    Guid ServiceOfferingId,
    string? RequesterNote) : IRequest<ServiceRequestDto>;
