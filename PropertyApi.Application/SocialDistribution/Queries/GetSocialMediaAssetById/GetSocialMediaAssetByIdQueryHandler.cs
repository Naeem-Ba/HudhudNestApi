using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialMediaAssetById;

public sealed class GetSocialMediaAssetByIdQueryHandler : IRequestHandler<GetSocialMediaAssetByIdQuery, SocialMediaAssetDto>
{
    private readonly ISocialMediaAssetRepository _assets;

    public GetSocialMediaAssetByIdQueryHandler(ISocialMediaAssetRepository assets) => _assets = assets;

    public async Task<SocialMediaAssetDto> Handle(GetSocialMediaAssetByIdQuery request, CancellationToken ct)
    {
        var asset = await _assets.GetByIdAsync(request.AssetId, ct)
            ?? throw new NotFoundException("الصورة الاجتماعية غير موجودة.");

        return ToDto(asset);
    }

    private static SocialMediaAssetDto ToDto(SocialMediaAsset a) => new(
        a.Id, a.PropertyId, a.PublicationId, a.Platform, a.AssetType, a.TemplateId, a.TemplateVersion,
        a.FileUrl, a.Width, a.Height, a.MimeType, a.FileSizeBytes, a.Checksum, a.Status, a.CreatedAt);
}
