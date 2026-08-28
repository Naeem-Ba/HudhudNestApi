using MediatR;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.Queries.GetMyServiceRequests;

/// <summary>"My service requests" as the requester — see GetProviderServiceRequestsQuery for
/// the provider-side inbox equivalent.</summary>
public sealed record GetMyServiceRequestsQuery(
    Guid RequesterId,
    ServiceRequestStatus? StatusFilter = null) : IRequest<IReadOnlyList<ServiceRequestDto>>;

public sealed class GetMyServiceRequestsQueryHandler
    : IRequestHandler<GetMyServiceRequestsQuery, IReadOnlyList<ServiceRequestDto>>
{
    private readonly IServiceRequestRepository _requests;

    public GetMyServiceRequestsQueryHandler(IServiceRequestRepository requests)
        => _requests = requests;

    public async Task<IReadOnlyList<ServiceRequestDto>> Handle(
        GetMyServiceRequestsQuery request, CancellationToken ct)
    {
        var requests = await _requests.GetByRequesterIdAsync(request.RequesterId, ct);

        return requests
            .Where(r => request.StatusFilter is null || r.Status == request.StatusFilter)
            .OrderByDescending(r => r.CreatedAt)
            .Select(ServiceMapper.ToDto)
            .ToList()
            .AsReadOnly();
    }
}
