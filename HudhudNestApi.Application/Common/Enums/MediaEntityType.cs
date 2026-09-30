namespace HudhudNestApi.Application.Common.Enums;

/// <summary>
/// The owning entity type for a piece of uploaded media. Used by <see
/// cref="Interfaces.IMediaFolderBuilder"/> to compute a Cloudinary folder — this is the only
/// input the folder path is derived from, alongside the entity's own id, never anything supplied
/// by a client request.
/// </summary>
public enum MediaEntityType
{
    Agency,
    User,
    Property,
    ShortStayListing,
    Investment,
    ServiceRequest,

    /// <summary>System-generated derivative assets (e.g. social-share images) that are produced
    /// from a source entity rather than uploaded by a user for it.</summary>
    Social
}
