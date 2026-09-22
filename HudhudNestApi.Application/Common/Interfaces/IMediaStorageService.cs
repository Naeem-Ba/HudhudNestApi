using HudhudNestApi.Application.Common.Models;

namespace HudhudNestApi.Application.Common.Interfaces;

/// <summary>
/// Abstraction for external media storage.
/// Application/API code must depend on this contract instead of Cloudinary/Azure concrete classes.
/// No ASP.NET-specific or Cloudinary-specific types are exposed here.
/// </summary>
public interface IMediaStorageService
{
    Task<MediaUploadResult> UploadImageAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an image using the provider-specific identifier stored in the database.
    /// In the current Cloudinary implementation this value is the Cloudinary PublicId.
    /// </summary>
    Task DeleteImageAsync(
        string publicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optional storage-agnostic read operation. The current controller does not use it yet,
    /// but the contract allows future providers or proxy-download endpoints.
    /// </summary>
    Task<MediaFileResult?> GetImageAsync(
        string imageUrl,
        CancellationToken cancellationToken = default);
}

