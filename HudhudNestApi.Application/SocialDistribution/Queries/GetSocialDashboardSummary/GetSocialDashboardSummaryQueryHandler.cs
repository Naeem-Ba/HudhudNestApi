using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialDashboardSummary;

public sealed class GetSocialDashboardSummaryQueryHandler : IRequestHandler<GetSocialDashboardSummaryQuery, SocialDashboardSummaryDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationDeadLetterRepository _deadLetters;

    public GetSocialDashboardSummaryQueryHandler(ISocialPublicationRepository publications, ISocialPublicationDeadLetterRepository deadLetters)
    {
        _publications = publications;
        _deadLetters = deadLetters;
    }

    public async Task<SocialDashboardSummaryDto> Handle(GetSocialDashboardSummaryQuery request, CancellationToken ct)
    {
        var summary = await _publications.GetDashboardSummaryAsync(DateTime.UtcNow, ct);
        var unresolvedDeadLetters = await _deadLetters.GetPagedAsync(resolved: false, page: 1, pageSize: 1, ct);

        return summary with { DeadLetterUnresolved = unresolvedDeadLetters.TotalCount };
    }
}
