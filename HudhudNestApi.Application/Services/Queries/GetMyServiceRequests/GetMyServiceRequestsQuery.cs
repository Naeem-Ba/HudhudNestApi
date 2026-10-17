using MediatR;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Queries.GetMyServiceRequests;

/// <summary>"My service requests" as the requester — see GetProviderServiceRequestsQuery for
/// the provider-side inbox equivalent.</summary>
public sealed record GetMyServiceRequestsQuery(
    Guid RequesterId,
    ServiceRequestStatus? StatusFilter = null) : IRequest<IReadOnlyList<ServiceRequestDto>>;

public sealed class GetMyServiceRequestsQueryHandler
    : IRequestHandler<GetMyServiceRequestsQuery, IReadOnlyList<ServiceRequestDto>>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceRequestMessageRepository _messages;

    public GetMyServiceRequestsQueryHandler(IServiceRequestRepository requests, IServiceRequestMessageRepository messages)
    {
        _requests = requests;
        _messages = messages;
    }

    public async Task<IReadOnlyList<ServiceRequestDto>> Handle(
        GetMyServiceRequestsQuery request, CancellationToken ct)
    {
        var requests = await _requests.GetByRequesterIdAsync(request.RequesterId, ct);

        var visible = requests
            .Where(r => request.StatusFilter is null || r.Status == request.StatusFilter)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var stats = await _messages.GetStatsAsync(visible.Select(r => r.Id).ToList(), ServiceParty.Requester, ct);

        return visible
            .Select(r => ServiceMapper.ToDto(
                r, ServiceRequestAudience.Requester, stats.GetValueOrDefault(r.Id, ServiceRequestMessageStats.Empty)))
            .ToList()
            .AsReadOnly();
    }
}
