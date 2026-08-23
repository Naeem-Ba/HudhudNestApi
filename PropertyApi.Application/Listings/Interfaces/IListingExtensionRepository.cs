using PropertyApi.Domain.Transactions.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

/// <summary>
/// Persistence seam for the single-listing extension fee. Kept separate from
/// IPropertyRepository because it deals in Transactions, not listings — a listing
/// repository that also writes financial records would blur an audit boundary.
/// </summary>
public interface IListingExtensionRepository
{
    /// <summary>
    /// Returns this owner's outstanding (Pending) extension fee for this listing, if any.
    ///
    /// Used to make the "request an extension" call idempotent: an owner who taps the
    /// button three times must end up owing one dollar, not three. Returns null when there
    /// is nothing outstanding.
    /// </summary>
    Task<Transaction?> GetPendingExtensionFeeAsync(
        Guid propertyId,
        Guid payerId,
        CancellationToken ct = default);

    Task<Transaction?> GetByIdAsync(Guid transactionId, CancellationToken ct = default);

    Task AddAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>
    /// Resolves the seeded USD currency row (id and its stored rate), because
    /// Transaction.Create requires a CurrencyId and freezes an exchange rate onto the
    /// record. The fee is denominated in USD, which CurrencySeed marks as the base
    /// currency, so the rate is expected to be 1 — but it is read rather than assumed,
    /// since a financial record must reflect what the system actually held at the time.
    /// </summary>
    Task<(int CurrencyId, decimal ExchangeRateToUsd)> GetUsdCurrencyAsync(
        CancellationToken ct = default);
}
