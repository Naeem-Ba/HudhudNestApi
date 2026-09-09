using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.ListSocialChannels;

public sealed record ListSocialChannelsQuery : IRequest<IReadOnlyList<SocialChannelDto>>;
