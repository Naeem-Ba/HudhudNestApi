using MediatR;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Commands.CreateServiceOffering;

public sealed record CreateServiceOfferingCommand(
    Guid ActorUserId,
    ServiceCategory Category,
    string Title,
    string? Description,
    decimal? BasePrice,
    int? CurrencyId,
    int? EstimatedDurationDays) : IRequest<ServiceOfferingDto>;
