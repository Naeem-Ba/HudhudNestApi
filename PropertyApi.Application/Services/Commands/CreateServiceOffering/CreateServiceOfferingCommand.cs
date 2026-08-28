using MediatR;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.Commands.CreateServiceOffering;

public sealed record CreateServiceOfferingCommand(
    Guid ActorUserId,
    ServiceCategory Category,
    string Title,
    string? Description,
    decimal? BasePrice,
    int? CurrencyId,
    int? EstimatedDurationDays) : IRequest<ServiceOfferingDto>;
