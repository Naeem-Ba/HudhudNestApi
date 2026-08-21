namespace PropertyApi.Domain.Transactions.Enums;

/// <summary>
/// The kind of financial movement a Transaction record represents.
/// Kept as an enum (instead of a free-text string) so invalid values are
/// rejected at compile time and cannot silently drift between call sites.
/// </summary>
public enum TransactionType
{
    /// <summary>Security/holding deposit paid by a tenant or buyer.</summary>
    Deposit = 0,

    /// <summary>Recurring rent payment for a rental listing.</summary>
    RentPayment = 1,

    /// <summary>One-time payment for a sale listing.</summary>
    SalePayment = 2,

    /// <summary>Money returned to the payer (full or partial).</summary>
    Refund = 3,

    /// <summary>Platform/agent commission fee.</summary>
    CommissionFee = 4,
}
