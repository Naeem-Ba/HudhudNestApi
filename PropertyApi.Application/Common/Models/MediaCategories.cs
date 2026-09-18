namespace PropertyApi.Application.Common.Models;

/// <summary>
/// Standard media-category slugs used with <see cref="Interfaces.IMediaFolderBuilder"/>. Keeping
/// these centralized avoids each feature inventing its own casing/spelling for the same concept.
/// </summary>
public static class MediaCategories
{
    public const string Logo = "logo";
    public const string Profile = "profile";
    public const string Images = "images";
    public const string Photos = "photos";
    public const string Documents = "documents";
    public const string Share = "share";
}
