using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialPublicationStatusHistory;

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
