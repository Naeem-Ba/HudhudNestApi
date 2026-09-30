using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Interfaces;

namespace HudhudNestApi.Infrastructure.SocialDistribution.Assets;

/// <summary>
/// Adapts the existing general-purpose <see cref="IMediaStorageService"/> (Cloudinary today) to
/// the narrower <see cref="ISocialMediaAssetStorage"/> Port this bounded context depends on —
/// reuses the same storage provider/credentials as property photo uploads without this context
/// coupling to Cloudinary directly.
/// </summary>
public sealed class MediaSocialMediaAssetStorage : ISocialMediaAssetStorage
{
    private readonly IMediaStorageService _mediaStorage;

    public MediaSocialMediaAssetStorage(IMediaStorageService mediaStorage) => _mediaStorage = mediaStorage;

    public async Task<(string Url, string StorageKey)> SaveAsync(
        byte[] content, string fileName, string contentType, string folder, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(content, writable: false);
        var result = await _mediaStorage.UploadImageAsync(stream, fileName, contentType, folder, ct);

        if (!result.Succeeded)
            throw new InvalidOperationException($"فشل رفع الصورة الاجتماعية: {result.ErrorMessage}");

        return (result.Url!, result.PublicId!);
    }
}
