using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListSocialChannels;

public sealed record ListSocialChannelsQuery : IRequest<IReadOnlyList<SocialChannelDto>>;
