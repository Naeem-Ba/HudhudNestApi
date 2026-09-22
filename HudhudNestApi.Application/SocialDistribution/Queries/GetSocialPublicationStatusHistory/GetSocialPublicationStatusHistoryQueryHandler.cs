using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialPublicationStatusHistory;

public sealed class GetSocialPublicationStatusHistoryQueryHandler
    : IRequestHandler<GetSocialPublicationStatusHistoryQuery, IReadOnlyList<SocialPublicationStatusHistoryDto>>
{
    private readonly ISocialPublicationStatusHistoryRepository _history;

    public GetSocialPublicationStatusHistoryQueryHandler(ISocialPublicationStatusHistoryRepository history) => _history = history;

    public async Task<IReadOnlyList<SocialPublicationStatusHistoryDto>> Handle(GetSocialPublicationStatusHistoryQuery request, CancellationToken ct)
    {
        var history = await _history.GetByPublicationIdAsync(request.PublicationId, ct);
        return history.Select(SocialDistributionMapper.ToDto).ToList();
    }
}
