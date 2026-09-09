using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.ListSocialChannels;

public sealed class ListSocialChannelsQueryHandler : IRequestHandler<ListSocialChannelsQuery, IReadOnlyList<SocialChannelDto>>
{
    private readonly ISocialChannelRepository _channels;

    public ListSocialChannelsQueryHandler(ISocialChannelRepository channels) => _channels = channels;

    public async Task<IReadOnlyList<SocialChannelDto>> Handle(ListSocialChannelsQuery request, CancellationToken ct)
    {
        var channels = await _channels.ListAsync(ct);
        return channels.Select(SocialDistributionMapper.ToDto).ToList();
    }
}
