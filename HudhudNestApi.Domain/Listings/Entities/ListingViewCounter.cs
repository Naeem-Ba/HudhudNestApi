using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Domain.Listings.Entities;

/// <summary>
/// The running total of counted views for one listing. Deliberately a table of its own instead of a
/// column on <c>Properties</c>/<c>ShortStayListings</c>: both of those carry an <c>xmin</c>
/// optimistic-concurrency token, so bumping a counter on the listing row would make every owner
/// edit (and every background sweep) that overlaps a view fail with a concurrency conflict, and it
/// would rewrite a wide, heavily indexed row on every view. Here the hot row is tiny, has no other
/// writers, and the listing rows stay untouched.
///
/// The value is only ever changed by <c>IListingViewRepository.TryRecordAsync</c> (an atomic
/// upsert-increment in the same statement as the dedupe-ledger insert), never by loading and
/// saving this entity, hence no setters and no factory.
/// </summary>
public sealed class ListingViewCounter
{
    public ListingKind Kind { get; private set; }
    public Guid ListingId { get; private set; }
    public long ViewCount { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private ListingViewCounter() { }
}
