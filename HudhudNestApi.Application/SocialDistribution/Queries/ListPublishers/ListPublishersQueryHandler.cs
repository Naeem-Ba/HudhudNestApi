using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListPublishers;

public sealed class ListPublishersQueryHandler : IRequestHandler<ListPublishersQuery, IReadOnlyList<SocialPublisherInfoDto>>
{
    private readonly ISocialPublisherRegistry _registry;

    public ListPublishersQueryHandler(ISocialPublisherRegistry registry) => _registry = registry;

    public Task<IReadOnlyList<SocialPublisherInfoDto>> Handle(ListPublishersQuery request, CancellationToken ct)
    {
        IReadOnlyList<SocialPublisherInfoDto> result = _registry.SupportedPlatforms
            .Select(platform =>
            {
                var publisher = _registry.TryGetPublisher(platform)!;
                return new SocialPublisherInfoDto(platform, publisher.GetCapabilities(), publisher.IsLive);
            })
            .ToList();

        return Task.FromResult(result);
    }
}
