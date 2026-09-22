using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record SocialMediaAssetDto(
    Guid Id,
    Guid PropertyId,
    Guid? PublicationId,
    SocialPlatform Platform,
    SocialAssetType AssetType,
    string TemplateId,
    int TemplateVersion,
    string FileUrl,
    int Width,
    int Height,
    string MimeType,
    long FileSizeBytes,
    string Checksum,
    SocialMediaAssetStatus Status,
    DateTime CreatedAt);
