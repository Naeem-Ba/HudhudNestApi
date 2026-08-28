using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;

namespace PropertyApi.Application.Services.Queries.GetServiceRequestStatusHistory;

public sealed record GetServiceRequestStatusHistoryQuery(
    Guid ServiceRequestId,
    Guid ActorUserId,
    bool IsAdmin) : IRequest<IReadOnlyList<ServiceRequestStatusHistoryDto>>;

public sealed class GetServiceRequestStatusHistoryQueryHandler
    : IRequestHandler<GetServiceRequestStatusHistoryQuery, IReadOnlyList<ServiceRequestStatusHistoryDto>>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestStatusHistoryRepository _history;

    public GetServiceRequestStatusHistoryQueryHandler(
        IServiceRequestRepository requests,
        IServiceProviderRepository providers,
        IServiceRequestStatusHistoryRepository history)
    {
        _requests = requests;
        _providers = providers;
        _history = history;
    }

    public async Task<IReadOnlyList<ServiceRequestStatusHistoryDto>> Handle(
        GetServiceRequestStatusHistoryQuery request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        if (!request.IsAdmin && serviceRequest.RequesterId != request.ActorUserId)
        {
            var provider = await _providers.GetByIdAsync(serviceRequest.ServiceProviderId, ct);
            if (provider is null || provider.UserId != request.ActorUserId)
                throw new ForbiddenException("لا يمكنك عرض سجل طلب خدمة لا يخصّك.");
        }

        var entries = await _history.GetByServiceRequestIdAsync(serviceRequest.Id, ct);
        return entries
            .OrderBy(e => e.CreatedAt)
            .Select(ServiceMapper.ToDto)
            .ToList()
            .AsReadOnly();
    }
}
