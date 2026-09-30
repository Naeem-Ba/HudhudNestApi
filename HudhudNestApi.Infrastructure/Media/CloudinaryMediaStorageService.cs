using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;

namespace HudhudNestApi.Infrastructure.Media;

public sealed class CloudinaryMediaStorageService : IMediaStorageService
{
    private readonly HttpClient _httpClient;
    private readonly Cloudinary _cloudinary;
    private readonly string? _folderPrefix;

    public CloudinaryMediaStorageService(
        IOptions<CloudinaryOptions> options,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;

        var value = options.Value;

        if (string.IsNullOrWhiteSpace(value.CloudName) ||
            string.IsNullOrWhiteSpace(value.ApiKey) ||
            string.IsNullOrWhiteSpace(value.ApiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary configuration is missing. Configure either " +
                "CLOUDINARY_URL or Cloudinary__CloudName, " +
                "Cloudinary__ApiKey and Cloudinary__ApiSecret.");
        }

        var account = new Account(
            value.CloudName,
            value.ApiKey,
            value.ApiSecret);

        _cloudinary = new Cloudinary(account);
        _folderPrefix = string.IsNullOrWhiteSpace(value.FolderPrefix) ? null : value.FolderPrefix;
    }

    public async Task<MediaUploadResult> UploadImageAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.CanSeek && content.Length == 0)
        {
            throw new InvalidOperationException(
                "Cannot upload an empty file.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(
                "File name is required.",
                nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException(
                "Folder is required.",
                nameof(folder));
        }

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, content),
            Folder = _folderPrefix is null ? folder : $"{_folderPrefix}/{folder}",
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false
        };

        var result = await _cloudinary.UploadAsync(
            uploadParams,
            cancellationToken);

        if (result.Error is not null)
        {
            return MediaUploadResult.Failed(result.Error.Message);
        }

        if (result.SecureUrl is null ||
            string.IsNullOrWhiteSpace(result.PublicId))
        {
            return MediaUploadResult.Failed(
                "Cloudinary upload did not return a valid URL or PublicId.");
        }

        return MediaUploadResult.Success(
            result.SecureUrl.AbsoluteUri,
            result.PublicId);
    }

    public async Task DeleteImageAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            throw new ArgumentException(
                "PublicId is required.",
                nameof(publicId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _cloudinary.DestroyAsync(
            new DeletionParams(publicId));

        cancellationToken.ThrowIfCancellationRequested();

        if (result.Error is not null)
        {
            throw new InvalidOperationException(result.Error.Message);
        }
    }

    public async Task<MediaFileResult?> GetImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException(
                "Image URL is required.",
                nameof(imageUrl));
        }

        if (!Uri.TryCreate(
                imageUrl,
                UriKind.Absolute,
                out var parsedImageUri))
        {
            throw new ArgumentException(
                "Image URL must be a valid absolute URL.",
                nameof(imageUrl));
        }

        using var response = await _httpClient.GetAsync(
            parsedImageUri,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsByteArrayAsync(
            cancellationToken);

        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? "application/octet-stream";

        var fileName = Path.GetFileName(parsedImageUri.AbsolutePath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "image";
        }

        return new MediaFileResult(
            content,
            contentType,
            fileName);
    }
}