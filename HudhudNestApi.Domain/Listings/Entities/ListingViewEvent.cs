using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Domain.Listings.Entities;

/// <summary>
/// One counted view of a listing's detail page: the dedupe ledger and the history behind the
/// owner statistics. A row exists only for a view that was actually added to the listing's
/// <c>ViewCount</c>; the unique key (kind, listing, viewer, bucket) is what makes the increment
/// idempotent under retries and concurrent requests.
///
/// Privacy: there is no user id, IP address or user agent here. <see cref="ViewerKeyHash"/> is an
/// HMAC (keyed with a server secret, never reversible without it) of the signed-in user id or of an
/// opaque random visitor token, so it can tell "the same visitor again" without identifying anyone.
/// Rows are purged after the retention window; the running total lives on the listing itself and
/// survives the purge.
///
/// Not a <see cref="Common.Entities.BaseEntity"/> on purpose: no soft delete, rows are hard-deleted
/// by the retention sweep.
/// </summary>
public sealed class ListingViewEvent
{
    public const int ViewerKeyHashLength = 64;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public ListingKind Kind { get; private set; }
    public Guid ListingId { get; private set; }
    public string ViewerKeyHash { get; private set; } = string.Empty;

    /// <summary>Cooldown window number: <c>floor(unixSeconds / cooldownSeconds)</c> at the time of the view.</summary>
    public long Bucket { get; private set; }

    public DateTime ViewedAt { get; private set; }

    private ListingViewEvent() { }

    public static ListingViewEvent Create(
        ListingKind kind, Guid listingId, string viewerKeyHash, long bucket, DateTime viewedAtUtc)
    {
        if (listingId == Guid.Empty)
            throw new DomainException("ListingId is required.");

        if (!Enum.IsDefined(kind))
            throw new DomainException("Unknown listing kind.");

        if (string.IsNullOrEmpty(viewerKeyHash) || viewerKeyHash.Length != ViewerKeyHashLength)
            throw new DomainException("ViewerKeyHash must be a 64-character hex digest.");

        return new ListingViewEvent
        {
            Kind = kind,
            ListingId = listingId,
            ViewerKeyHash = viewerKeyHash,
            Bucket = bucket,
            ViewedAt = viewedAtUtc
        };
    }
}
