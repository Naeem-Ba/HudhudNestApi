using HudhudNestApi.Application.Common.Enums;

namespace HudhudNestApi.Application.Common.Interfaces;

/// <summary>
/// Computes the Cloudinary (or any future provider's) storage folder for a piece of media from
/// the owning entity's type and id plus a media category — never from client input. Handlers
/// authorize the caller against the entity first, then hand this builder that entity's own id;
/// there is no path by which a request can choose its own folder.
/// </summary>
public interface IMediaFolderBuilder
{
    /// <summary>
    /// Builds a deterministic folder path such as "hudhudnest/properties/{propertyId}/images".
    /// </summary>
    /// <param name="entityType">The kind of entity the media belongs to.</param>
    /// <param name="entityId">The owning entity's id. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="mediaCategory">A short lowercase slug (e.g. "logo", "profile", "images",
    /// "photos", "documents") naming the kind of media within the entity's space. See <see
    /// cref="Models.MediaCategories"/> for the standard values.</param>
    string BuildFolder(MediaEntityType entityType, Guid entityId, string mediaCategory);
}
