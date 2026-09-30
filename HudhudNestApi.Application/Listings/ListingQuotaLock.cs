namespace HudhudNestApi.Application.Listings;

/// <summary>
/// Derives PostgreSQL advisory-lock keys for the listing-quota check
/// (RELEASE-BLOCKERS-AR.md B-10).
///
/// CreatePropertyCommandHandler's quota check counts existing rows and then inserts one
/// more — two requests for the same owner/agency racing that window can both read the same
/// "count so far" and both proceed, landing one over the limit. Unlike B-9's Property.xmin
/// (which protects one row against two writers), this is a count across many rows with no
/// single row to attach a concurrency token to, and unlike B-3's agency-pooled limit (which
/// can be any configured number), this cannot be expressed as a single-row unique constraint
/// either. An advisory lock keyed by owner/agency serializes concurrent attempts for the same
/// key without blocking unrelated owners/agencies from creating listings at the same time.
///
/// A GUID is 128 bits and an advisory lock key is 64 — the two owner/agency key spaces are
/// XORed with different constants so a hypothetical truncation collision between an owner id
/// and an unrelated agency id cannot make one lock stand in for the other.
/// </summary>
public static class ListingQuotaLock
{
    private const long OwnerDomainTag = 0x4C51_4F57_4E45_5231; // "LQOWNER1" (ASCII bytes)
    private const long AgencyDomainTag = 0x4C51_4147_4E43_5931; // "LQAGNCY1" (ASCII bytes)

    public static long ForOwner(Guid ownerId) => Fold(ownerId) ^ OwnerDomainTag;

    public static long ForAgency(Guid agencyId) => Fold(agencyId) ^ AgencyDomainTag;

    private static long Fold(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
