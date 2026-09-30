using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialMediaAssetById;

public sealed record GetSocialMediaAssetByIdQuery(Guid AssetId) : IRequest<SocialMediaAssetDto>;
