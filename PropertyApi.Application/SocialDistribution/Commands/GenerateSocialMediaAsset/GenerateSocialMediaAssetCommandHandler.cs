using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Services;
using PropertyApi.Domain.SocialDistribution.Templates;

namespace PropertyApi.Application.SocialDistribution.Commands.GenerateSocialMediaAsset;

public sealed class GenerateSocialMediaAssetCommandHandler : IRequestHandler<GenerateSocialMediaAssetCommand, GeneratedSocialAssetResult>
{
    private readonly ISocialMediaAssetGenerator _generator;

    public GenerateSocialMediaAssetCommandHandler(ISocialMediaAssetGenerator generator) => _generator = generator;

    public Task<GeneratedSocialAssetResult> Handle(GenerateSocialMediaAssetCommand request, CancellationToken ct) =>
        _generator.GenerateAsync(
            new GenerateSocialAssetRequest(
                request.PropertyId, request.Platform, SocialAssetTemplateRenderer.TemplateId, request.Language,
                request.ImageUrls, request.Title, request.Body, request.AssetType),
            forceRegenerate: false,
            ct);
}
