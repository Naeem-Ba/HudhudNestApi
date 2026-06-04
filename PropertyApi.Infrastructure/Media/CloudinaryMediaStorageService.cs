using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Npgsql.BackendMessages;
using System.Security.Principal;

namespace PropertyApi.Infrastructure.Media;

public sealed class CloudinaryMediaStorageService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryMediaStorageService(IOptions<CloudinaryOptions> options)
    {
        var value = options.Value;

        if (string.IsNullOrWhiteSpace(value.CloudName) ||
            string.IsNullOrWhiteSpace(value.ApiKey) ||
            string.IsNullOrWhiteSpace(value.ApiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary configuration is missing. Configure Cloudinary__CloudName, Cloudinary__ApiKey and Cloudinary__ApiSecret.");
        }

        var account = new Account(
            value.CloudName,
            value.ApiKey,
            value.ApiSecret);

        _cloudinary = new Cloudinary(account);
    }

    public async Task<ImageUploadResult> UploadAsync(
        IFormFile file,
        string folder,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            throw new InvalidOperationException("Cannot upload empty file.");

        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false
        };

        return await _cloudinary.UploadAsync(uploadParams, cancellationToken);
    }

    public async Task<DeletionResult> DeleteAsync(
     string publicId,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId))
            throw new ArgumentException("PublicId is required.", nameof(publicId));

        cancellationToken.ThrowIfCancellationRequested();

        return await _cloudinary.DestroyAsync(
            new DeletionParams(publicId));
    }
}