using HudhudNestApi.Domain.Transactions.Entities;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Listings.Interfaces;

/// <summary>
/// Persistence seam for the platform fees an owner pays on their own listing — today the
/// single-listing extension fee and the featured-placement fee. Kept separate from
/// IPropertyRepository because it deals in Transactions, not listings: a listing
/// repository that also writes financial records would blur an audit boundary.
///
/// Named for fees rather than for one fee: it started as IListingExtensionRepository and
/// was renamed when featured placement began using the identical Pending-then-confirm
/// path. One seam, parameterised by TransactionType, beats two near-identical ones that
/// have to be kept in step by hand.
/// </summary>
public interface IListingFeeRepository
{
    /// <summary>
    /// Returns this owner's outstanding (Pending) fee of the given type for this listing,
    /// if any.
    ///
    /// Used to make "request a fee" calls idempotent: an owner who taps the button three
    /// times must end up owing one fee, not three. Returns null when there is nothing
    /// outstanding.
    ///
    /// The type is a parameter rather than a filter the caller applies afterwards, because
    /// a pending extension fee and a pending featured fee can legitimately coexist on the
    /// same listing — an owner may owe for both at once, and neither should suppress the
    /// other.
    /// </summary>
    Task<Transaction?> GetPendingFeeAsync(
        Guid propertyId,
        Guid payerId,
        TransactionType transactionType,
        CancellationToken ct = default);

    Task<Transaction?> GetByIdAsync(Guid transactionId, CancellationToken ct = default);

    Task AddAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>
    /// Resolves the seeded USD currency row (id and its stored rate), because
    /// Transaction.Create requires a CurrencyId and freezes an exchange rate onto the
    /// record. Fees are denominated in USD, which CurrencySeed marks as the base
    /// currency, so the rate is expected to be 1 — but it is read rather than assumed,
    /// since a financial record must reflect what the system actually held at the time.
    /// </summary>
    Task<(int CurrencyId, decimal ExchangeRateToUsd)> GetUsdCurrencyAsync(
        CancellationToken ct = default);
}
