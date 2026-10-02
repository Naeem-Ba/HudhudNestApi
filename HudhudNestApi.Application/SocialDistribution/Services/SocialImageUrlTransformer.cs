using System.Diagnostics.CodeAnalysis;

namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>
/// Normalises a property photo to what every target platform accepts, at publish time only.
/// Instagram's Content Publishing API takes JPEG only and Telegram's <c>sendPhoto</c> by URL caps
/// the file at 5 MB, while the owner's upload may be a large PNG/WebP/HEIC. A Cloudinary image
/// delivery URL gets <c>f_jpg,q_auto,w_1440,c_limit</c> inserted right after <c>/image/upload/</c>
/// (chained before any transformation already in the URL) and its extension forced to <c>.jpg</c>;
/// <c>c_limit</c> only ever shrinks, so a small photo is never upscaled. Anything that is not a
/// Cloudinary image delivery URL is returned untouched — the stored content is never rewritten,
/// only the request handed to the publisher.
/// </summary>
public static class SocialImageUrlTransformer
{
    private const string Transformation = "f_jpg,q_auto,w_1440,c_limit";
    private const string UploadSegment = "/image/upload/";
    private const string CloudinaryDomain = "cloudinary.com";

    [return: NotNullIfNotNull(nameof(imageUrl))]
    public static string? ForPublishing(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || !IsCloudinaryHost(imageUrl))
            return imageUrl;

        var queryStart = imageUrl.IndexOfAny(['?', '#']);
        var path = queryStart < 0 ? imageUrl : imageUrl[..queryStart];
        var suffix = queryStart < 0 ? string.Empty : imageUrl[queryStart..];

        var uploadIndex = path.IndexOf(UploadSegment, StringComparison.Ordinal);
        if (uploadIndex < 0)
            return imageUrl;

        var afterUpload = uploadIndex + UploadSegment.Length;
        if (path.AsSpan(afterUpload).StartsWith($"{Transformation}/", StringComparison.Ordinal))
            return imageUrl;

        return $"{path[..afterUpload]}{Transformation}/{ForceJpegExtension(path[afterUpload..])}{suffix}";
    }

    private static bool IsCloudinaryHost(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        return uri.Host.Equals(CloudinaryDomain, StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith($".{CloudinaryDomain}", StringComparison.OrdinalIgnoreCase);
    }

    private static string ForceJpegExtension(string pathAfterUpload)
    {
        var lastSlash = pathAfterUpload.LastIndexOf('/');
        var lastDot = pathAfterUpload.LastIndexOf('.');

        return lastDot > lastSlash
            ? $"{pathAfterUpload[..lastDot]}.jpg"
            : pathAfterUpload;
    }
}
