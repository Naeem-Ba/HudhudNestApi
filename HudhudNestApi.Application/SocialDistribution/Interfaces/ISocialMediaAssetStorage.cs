namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// Storage Port for generated assets (Phase 7 spec §22) — kept separate from the general-purpose
/// <c>IMediaStorageService</c> (property photo uploads) so this bounded context depends on its own
/// narrow contract rather than reaching into Listings' media concerns; the Infrastructure
/// implementation is free to delegate to the same underlying provider.
/// </summary>
public interface ISocialMediaAssetStorage
{
    /// <summary>Uploads the rendered asset bytes and returns its public URL and a provider-specific storage key (for later deletion/management).</summary>
    Task<(string Url, string StorageKey)> SaveAsync(
        byte[] content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken ct = default);
}
