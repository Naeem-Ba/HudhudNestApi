using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;

namespace HudhudNestApi.Application.Services.Queries.GetServiceRequestById;

/// <summary>ActorUserId enforces that only the requester, the owning provider, or an admin can
/// view a request — never a bare "get by id" open to anyone with the GUID.</summary>
public sealed record GetServiceRequestByIdQuery(
    Guid ServiceRequestId,
    Guid ActorUserId,
    bool IsAdmin) : IRequest<ServiceRequestDto>;

public sealed class GetServiceRequestByIdQueryHandler
    : IRequestHandler<GetServiceRequestByIdQuery, ServiceRequestDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;

    public GetServiceRequestByIdQueryHandler(
        IServiceRequestRepository requests, IServiceProviderRepository providers)
    {
        _requests = requests;
        _providers = providers;
    }

    public async Task<ServiceRequestDto> Handle(GetServiceRequestByIdQuery request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        if (!request.IsAdmin && serviceRequest.RequesterId != request.ActorUserId)
        {
            var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct);
            if (provider is null || provider.UserId != request.ActorUserId)
                throw new ForbiddenException("لا يمكنك عرض طلب خدمة لا يخصّك.");
        }

        return ServiceMapper.ToDto(serviceRequest);
    }
}
