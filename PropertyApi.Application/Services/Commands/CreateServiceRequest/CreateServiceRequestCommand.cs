using MediatR;
using PropertyApi.Application.Services.DTOs;

namespace PropertyApi.Application.Services.Commands.CreateServiceRequest;

public sealed record CreateServiceRequestCommand(
    Guid PropertyId,
    Guid RequesterId,
    Guid ServiceOfferingId,
    string? RequesterNote) : IRequest<ServiceRequestDto>;
