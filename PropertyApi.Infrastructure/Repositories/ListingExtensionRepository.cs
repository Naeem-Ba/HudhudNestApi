using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Transactions.Entities;
using PropertyApi.Domain.Transactions.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class ListingExtensionRepository : IListingExtensionRepository
{
    private readonly AppDbContext _db;

    public ListingExtensionRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Transaction?> GetPendingExtensionFeeAsync(
        Guid propertyId,
        Guid payerId,
        CancellationToken ct = default)
    {
        // Tracked, not AsNoTracking: the caller may hand this straight back as a quote, but
        // ConfirmListingExtensionPayment mutates the same row through GetByIdAsync, and
        // returning a detached entity from one path and a tracked one from the other is the
        // kind of inconsistency that produces a silent no-op save later.
        return await _db.Transactions
            .Where(t =>
                t.PropertyId == propertyId &&
                t.PayerId == payerId &&
                t.TransactionType == TransactionType.ListingExtensionFee &&
                t.Status == TransactionStatus.Pending)
            .OrderByDescending(t => t.TransactedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Transaction?> GetByIdAsync(Guid transactionId, CancellationToken ct = default)
    {
        return await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct);
    }

    public async Task AddAsync(Transaction transaction, CancellationToken ct = default)
    {
        await _db.Transactions.AddAsync(transaction, ct);
    }

    public async Task<(int CurrencyId, decimal ExchangeRateToUsd)> GetUsdCurrencyAsync(
        CancellationToken ct = default)
    {
        var usd = await _db.Currencies
            .AsNoTracking()
            .Where(c => c.Code == "USD")
            .Select(c => new { c.Id, c.ExchangeRateToUSD })
            .FirstOrDefaultAsync(ct);

        if (usd is null)
        {
            // CurrencySeed guarantees this row. If it is missing, the seed did not run —
            // recording a fee against a guessed currency id would corrupt the financial
            // audit trail, so fail loudly instead.
            throw new InvalidOperationException(
                "USD currency row is missing. CurrencySeed must run before listing extension fees can be recorded.");
        }

        return (usd.Id, usd.ExchangeRateToUSD);
    }
}
