using System.Text.RegularExpressions;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Common.Services;

/// <summary>
/// Deterministic, pure implementation of <see cref="IMediaFolderBuilder"/> — no I/O, so a
/// singleton is safe (same reasoning as the other stateless orchestration services registered in
/// <see cref="DependencyInjection"/>).
///
/// Folder shape: "hudhudnest/{entitySegment}/{entityId}/{mediaCategory}", e.g.
/// "hudhudnest/properties/3fa8.../images". Social (system-generated) assets nest under
/// "hudhudnest/system/social/{sourceEntityId}/{mediaCategory}" to keep them out of the
/// source entity's own user-facing folder while still being traceable back to it.
///
/// Root changed from "realestateworld" to "hudhudnest" on 2026-09-23 (brand rename). Only new
/// uploads land under the new root — existing Cloudinary assets keep their already-assigned
/// public_id/URL under the old "realestateworld/..." prefix and are unaffected, since this
/// builder only computes the destination folder for a fresh upload, never a lookup path for
/// existing media.
/// </summary>
public sealed partial class MediaFolderBuilder : IMediaFolderBuilder
{
    private const string Root = "hudhudnest";

    public string BuildFolder(MediaEntityType entityType, Guid entityId, string mediaCategory)
    {
        if (entityId == Guid.Empty)
            throw new ArgumentException("Entity id must not be empty.", nameof(entityId));

        if (string.IsNullOrWhiteSpace(mediaCategory) || !CategorySlugRegex().IsMatch(mediaCategory))
            throw new ArgumentException(
                "Media category must be a short lowercase slug (letters, digits, hyphens).",
                nameof(mediaCategory));

        var entitySegment = entityType switch
        {
            MediaEntityType.Agency => "agencies",
            MediaEntityType.User => "users",
            MediaEntityType.Property => "properties",
            MediaEntityType.ShortStayListing => "short-stay",
            MediaEntityType.Investment => "investments",
            MediaEntityType.ServiceRequest => "services",
            MediaEntityType.Social => "system/social",
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown media entity type.")
        };

        return $"{Root}/{entitySegment}/{entityId:D}/{mediaCategory}";
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex CategorySlugRegex();
}
