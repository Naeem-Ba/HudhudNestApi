using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Interfaces;

public interface IOfferRepository
{
    void Add(Offer offer);

    Task<Offer?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The single offer, if any, that is Active and within its date window right
    /// now. Callers must still check <c>IsCurrentlyRedeemable</c> for the redemption-count
    /// condition — this only applies the Status/date part of the filter at the database
    /// level so an ended-by-date offer is never even loaded.</summary>
    Task<Offer?> GetCurrentActiveAsync(DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<Offer>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically increments <c>RedeemedCount</c> via a single conditional
    /// <c>UPDATE ... WHERE RedeemedCount &lt; MaxRedemptions</c> (or no cap at all)
    /// statement — see Offer's class doc comment. Returns false, without changing anything,
    /// when the offer no longer has a free slot; the caller must not treat that as an
    /// error, only as "this particular claim did not go through".
    /// </summary>
    Task<bool> TryReserveRedemptionAsync(Guid offerId, CancellationToken ct = default);
}
