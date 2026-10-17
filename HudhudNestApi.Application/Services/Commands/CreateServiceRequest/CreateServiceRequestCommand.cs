using MediatR;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Commands.CreateServiceRequest;

/// <summary>
/// Opens a conversation with the provider. <paramref name="Intent"/> says which button the customer
/// pressed; the request is an inquiry until the customer confirms it. A null intent is the call of an
/// app version that predates the conversation stage: that request is confirmed on the spot, exactly as
/// it used to be, so an API deployed before the app never leaves an old client with a stuck inquiry.
/// </summary>
public sealed record CreateServiceRequestCommand(
    Guid PropertyId,
    Guid RequesterId,
    Guid ServiceOfferingId,
    string? RequesterNote,
    ServiceRequestIntent? Intent = null) : IRequest<ServiceRequestDto>;
