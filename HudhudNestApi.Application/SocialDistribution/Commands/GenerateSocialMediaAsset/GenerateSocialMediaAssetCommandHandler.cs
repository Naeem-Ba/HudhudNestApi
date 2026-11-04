using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Templates;

namespace HudhudNestApi.Application.SocialDistribution.Commands.GenerateSocialMediaAsset;

public sealed class GenerateSocialMediaAssetCommandHandler : IRequestHandler<GenerateSocialMediaAssetCommand, GeneratedSocialAssetResult>
{
    private readonly ISocialMediaAssetGenerator _generator;

    public GenerateSocialMediaAssetCommandHandler(ISocialMediaAssetGenerator generator) => _generator = generator;

    public async Task<GeneratedSocialAssetResult> Handle(GenerateSocialMediaAssetCommand request, CancellationToken ct)
    {
        try
        {
            return await _generator.GenerateAsync(
                new GenerateSocialAssetRequest(
                    request.PropertyId, request.Platform, SocialAssetTemplateRenderer.TemplateId, request.Language,
                    request.ImageUrls, request.Title, request.Body, request.AssetType),
                forceRegenerate: false,
                ct);
        }
        catch (SocialAssetGenerationException ex) when (ex.PropertyNotFound)
        {
            // P18-D02: unknown property is a client error (404), not a 500.
            throw new NotFoundException(ex.Message);
        }
    }
}
