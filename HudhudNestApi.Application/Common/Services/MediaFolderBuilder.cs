using System.Text.RegularExpressions;
using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Common.Services;

/// <summary>
/// Deterministic, pure implementation of <see cref="IMediaFolderBuilder"/> — no I/O, so a
/// singleton is safe (same reasoning as the other stateless orchestration services registered in
/// <see cref="DependencyInjection"/>).
///
/// Folder shape: "realestateworld/{entitySegment}/{entityId}/{mediaCategory}", e.g.
/// "realestateworld/properties/3fa8.../images". Social (system-generated) assets nest under
/// "realestateworld/system/social/{sourceEntityId}/{mediaCategory}" to keep them out of the
/// source entity's own user-facing folder while still being traceable back to it.
/// </summary>
public sealed partial class MediaFolderBuilder : IMediaFolderBuilder
{
    private const string Root = "realestateworld";

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
