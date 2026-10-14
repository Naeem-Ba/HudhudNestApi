namespace HudhudNestApi.Application.Common.Interfaces;

/// <summary>
/// Shrinks a stored listing photo in place to save storage. Used for closed deals (Sold/Rented)
/// whose photos are kept only as documentation. Kept apart from <see cref="IMediaStorageService"/>
/// because it is a maintenance operation, not part of the upload/delete/read contract every
/// storage fake implements.
/// </summary>
public interface IListingImageCompactor
{
    /// <summary>
    /// Replaces the stored image identified by <paramref name="publicId"/> with a smaller version
    /// under the same identifier. Idempotent in effect: running it again only shrinks it further
    /// within the provider's limits, and the caller guarantees it runs once per listing.
    /// </summary>
    Task<ImageCompactionResult> CompactAsync(string publicId, string currentUrl, CancellationToken cancellationToken = default);
}

/// <param name="Succeeded">False when the provider refused or failed; the caller retries on a later sweep.</param>
/// <param name="NewUrl">The URL of the replaced image when the provider issued a new one (a new version segment), else null.</param>
/// <param name="Error">Provider message for logging, never shown to a user.</param>
public sealed record ImageCompactionResult(bool Succeeded, string? NewUrl = null, string? Error = null)
{
    public static ImageCompactionResult Success(string? newUrl = null) => new(true, newUrl);

    public static ImageCompactionResult Failure(string error) => new(false, null, error);
}
