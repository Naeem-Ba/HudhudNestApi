using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.ListDeadLetters;

public sealed class ListDeadLettersQueryHandler : IRequestHandler<ListDeadLettersQuery, PagedResult<SocialPublicationDeadLetterDto>>
{
    private readonly ISocialPublicationDeadLetterRepository _deadLetters;

    public ListDeadLettersQueryHandler(ISocialPublicationDeadLetterRepository deadLetters) => _deadLetters = deadLetters;

    public async Task<PagedResult<SocialPublicationDeadLetterDto>> Handle(ListDeadLettersQuery request, CancellationToken ct)
    {
        var page = await _deadLetters.GetPagedAsync(request.Resolved, request.Page, request.PageSize, ct);

        return new PagedResult<SocialPublicationDeadLetterDto>
        {
            Items = page.Items.Select(SocialDistributionMapper.ToDto).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }
}
