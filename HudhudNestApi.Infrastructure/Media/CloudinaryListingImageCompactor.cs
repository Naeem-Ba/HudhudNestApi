using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Infrastructure.Media;

/// <summary>
/// Compresses a stored photo on Cloudinary by re-uploading it from its own delivery URL under the
/// same public id with an incoming transformation: only the transformed result is stored, so the
/// original bytes (and the storage they occupied) are replaced, not kept beside a derived copy.
/// Width is capped and quality lowered hard: a sold or rented listing's photos are a record of the
/// deal, not a showcase.
///
/// Cannot be exercised against a real Cloudinary account from the build environment; the sweep
/// that calls it is covered with a fake, and the first real run belongs on Staging (see the
/// Phase 7 report).
/// </summary>
public sealed class CloudinaryListingImageCompactor : IListingImageCompactor
{
    /// <summary>Longest side kept, in pixels.</summary>
    internal const int MaxSidePixels = 1000;

    /// <summary>Cloudinary quality (1-100) of the stored copy.</summary>
    internal const int Quality = 40;

    private readonly Cloudinary _cloudinary;

    public CloudinaryListingImageCompactor(IOptions<CloudinaryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;

        if (string.IsNullOrWhiteSpace(value.CloudName) ||
            string.IsNullOrWhiteSpace(value.ApiKey) ||
            string.IsNullOrWhiteSpace(value.ApiSecret))
        {
            throw new InvalidOperationException("Cloudinary configuration is missing.");
        }

        _cloudinary = new Cloudinary(new Account(value.CloudName, value.ApiKey, value.ApiSecret));
    }

    public async Task<ImageCompactionResult> CompactAsync(
        string publicId, string currentUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId) || string.IsNullOrWhiteSpace(currentUrl))
            return ImageCompactionResult.Failure("PublicId and URL are required.");

        cancellationToken.ThrowIfCancellationRequested();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(currentUrl),
            PublicId = publicId,
            Overwrite = true,
            Invalidate = true,
            UniqueFilename = false,
            UseFilename = false,
            Transformation = new Transformation()
                .Width(MaxSidePixels).Height(MaxSidePixels).Crop("limit")
                .Quality(Quality)
        };

        var result = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (result.Error is not null)
            return ImageCompactionResult.Failure(result.Error.Message);

        return ImageCompactionResult.Success(result.SecureUrl?.AbsoluteUri);
    }
}

/// <summary>
/// Used where media lives in memory (Staging smoke tests): there is nothing to shrink, but the
/// sweep must still be able to complete so its bookkeeping can be exercised end to end.
/// </summary>
public sealed class NoOpListingImageCompactor : IListingImageCompactor
{
    public Task<ImageCompactionResult> CompactAsync(
        string publicId, string currentUrl, CancellationToken cancellationToken = default)
        => Task.FromResult(ImageCompactionResult.Success());
}
