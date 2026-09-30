using System.Collections.Concurrent;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;

namespace HudhudNestApi.Infrastructure.Media;

public sealed class StagingSmokeMediaStorageService : IMediaStorageService
{
    private readonly ConcurrentDictionary<string, MediaFileResult> _files = new();

    public async Task<MediaUploadResult> UploadImageAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var publicId = Guid.NewGuid().ToString("N");
        _files[publicId] = new MediaFileResult(
            buffer.ToArray(),
            contentType,
            Path.GetFileName(fileName));

        return MediaUploadResult.Success(
            $"/api/staging-test-support/media/{publicId}",
            publicId);
    }

    public Task DeleteImageAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _files.TryRemove(publicId, out _);
        return Task.CompletedTask;
    }

    public Task<MediaFileResult?> GetImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var publicId = Uri.UnescapeDataString(imageUrl[(imageUrl.LastIndexOf('/') + 1)..]);
        return Task.FromResult(_files.GetValueOrDefault(publicId));
    }

    public MediaFileResult? GetByPublicId(string publicId) =>
        _files.GetValueOrDefault(publicId);
}
