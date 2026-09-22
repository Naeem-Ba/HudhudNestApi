using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Templates;

namespace HudhudNestApi.Application.SocialDistribution.Commands.RegenerateSocialMediaAsset;

public sealed class RegenerateSocialMediaAssetCommandHandler : IRequestHandler<RegenerateSocialMediaAssetCommand, GeneratedSocialAssetResult>
{
    private readonly ISocialMediaAssetRepository _assets;
    private readonly IPropertyRepository _properties;
    private readonly ISocialMediaAssetGenerator _generator;

    public RegenerateSocialMediaAssetCommandHandler(
        ISocialMediaAssetRepository assets, IPropertyRepository properties, ISocialMediaAssetGenerator generator)
    {
        _assets = assets;
        _properties = properties;
        _generator = generator;
    }

    public async Task<GeneratedSocialAssetResult> Handle(RegenerateSocialMediaAssetCommand request, CancellationToken ct)
    {
        var existing = await _assets.GetByIdAsync(request.AssetId, ct)
            ?? throw new NotFoundException("الصورة الاجتماعية غير موجودة.");

        var property = await _properties.GetByIdWithDetailsAsync(existing.PropertyId, ct)
            ?? throw new NotFoundException("العقار المرتبط بهذه الصورة لم يعد موجوداً.");

        var imageUrl = property.Images.FirstOrDefault(i => i.IsMain)?.Url
            ?? property.Images.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Url))?.Url;

        return await _generator.GenerateAsync(
            new GenerateSocialAssetRequest(
                existing.PropertyId, existing.Platform, SocialAssetTemplateRenderer.TemplateId, "ar",
                imageUrl is null ? [] : [imageUrl], property.Title, property.Description, existing.AssetType),
            forceRegenerate: true,
            ct);
    }
}
