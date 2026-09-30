namespace HudhudNestApi.Application.Common.Models;

/// <summary>
/// Storage-agnostic result returned after uploading a media file.
/// This type intentionally does not expose Cloudinary/Azure-specific objects.
/// </summary>
public sealed record MediaUploadResult(
    string? Url,
    string? PublicId,
    string? ErrorMessage)
{
    public bool Succeeded => string.IsNullOrWhiteSpace(ErrorMessage)
        && !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(PublicId);

    public static MediaUploadResult Success(string url, string publicId)
        => new(url, publicId, null);

    public static MediaUploadResult Failed(string errorMessage)
        => new(null, null, errorMessage);
}

