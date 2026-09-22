using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record GeneratedSocialAssetResult(
    Guid AssetId,
    SocialPlatform Platform,
    SocialAssetType AssetType,
    string FileUrl,
    int Width,
    int Height,
    string MimeType,
    long FileSizeBytes,
    string Checksum,
    string TemplateId,
    int TemplateVersion,
    bool Reused);
