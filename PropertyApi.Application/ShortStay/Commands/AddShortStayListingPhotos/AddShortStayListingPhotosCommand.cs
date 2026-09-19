using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.ShortStay.Commands.AddShortStayListingPhotos;

/// <summary>
/// Uploads one or more photos for a listing via the same Cloudinary-backed
/// IMediaStorageService the Property image pipeline already uses (UploadPropertyImageFileDto is
/// reused as-is — it's just a Stream/FileName/ContentType/Length tuple, not Property-specific).
///
/// Was a total gap until now: ShortStayListingPhoto/ShortStayListing.Photos existed since the
/// initial migration and PhotoUrls was already on the read DTO, but nothing ever populated it —
/// a listing could never have an actual photo. Photo delete/reorder/set-main are intentionally
/// NOT included in this pass (see final report's deferred-work section) — a host can re-upload
/// to fix ordering for now; this is scoped to "a listing can have photos at all".
/// </summary>
public sealed record AddShortStayListingPhotosCommand(
    Guid ListingId, Guid OwnerId, IReadOnlyList<UploadPropertyImageFileDto> Files)
    : IRequest<IReadOnlyList<string>>;
