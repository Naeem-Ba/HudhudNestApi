using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialMediaAssetById;

public sealed record GetSocialMediaAssetByIdQuery(Guid AssetId) : IRequest<SocialMediaAssetDto>;
