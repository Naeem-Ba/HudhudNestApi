using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.Queries.GetProviderServiceRequests;

/// <summary>The provider's inbox. ActorUserId is resolved to a ServiceProvider server-side —
/// a caller can never pass a ServiceProviderId directly and read someone else's inbox.</summary>
public sealed record GetProviderServiceRequestsQuery(
    Guid ActorUserId,
    ServiceRequestStatus? StatusFilter = null) : IRequest<IReadOnlyList<ServiceRequestDto>>;

public sealed class GetProviderServiceRequestsQueryHandler
    : IRequestHandler<GetProviderServiceRequestsQuery, IReadOnlyList<ServiceRequestDto>>
{
    private readonly IServiceProviderRepository _providers;
    private readonly IServiceRequestRepository _requests;

    public GetProviderServiceRequestsQueryHandler(
        IServiceProviderRepository providers, IServiceRequestRepository requests)
    {
        _providers = providers;
        _requests = requests;
    }

    public async Task<IReadOnlyList<ServiceRequestDto>> Handle(
        GetProviderServiceRequestsQuery request, CancellationToken ct)
    {
        var provider = await _providers.GetByUserIdAsync(request.ActorUserId, ct)
            ?? throw new NotFoundException("لا يوجد ملف مزوّد خدمة لهذا المستخدم.");

        var requests = await _requests.GetByServiceProviderIdAsync(provider.Id, ct);

        return requests
            .Where(r => request.StatusFilter is null || r.Status == request.StatusFilter)
            .OrderByDescending(r => r.CreatedAt)
            .Select(ServiceMapper.ToDto)
            .ToList()
            .AsReadOnly();
    }
}
