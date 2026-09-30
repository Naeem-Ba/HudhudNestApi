using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Domain.Transactions.Entities;

/// <summary>
/// Financial record for a single transaction tied to a property.
/// Why? To guarantee a full financial audit trail.
/// ExchangeRateUsed is important: it freezes the exchange rate at the time
/// of the transaction even if the live rate changes later.
/// </summary>
public class Transaction : BaseEntity
{
    public Guid? BookingId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid PayerId { get; private set; }
    public Guid ReceiverId { get; private set; }

    public TransactionType TransactionType { get; private set; }

    public decimal Amount { get; private set; }
    public int CurrencyId { get; private set; }

    /// <summary>Amount in USD — computed immediately from the exchange rate in effect and stored.</summary>
    public decimal AmountInUSD { get; private set; }

    /// <summary>
    /// Exchange rate at the moment the transaction was executed.
    /// This is different from BasePriceInUSD on Property: here we keep it
    /// because a financial transaction needs a historical snapshot.
    /// </summary>
    public decimal ExchangeRateUsed { get; private set; }

    public TransactionPaymentMethod PaymentMethod { get; private set; }

    public TransactionStatus Status { get; private set; } = TransactionStatus.Pending;

    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime TransactedAt { get; private set; }

    private Transaction() { }

    public static Transaction Create(
        Guid propertyId,
        Guid payerId,
        Guid receiverId,
        TransactionType transactionType,
        decimal amount,
        int currencyId,
        decimal exchangeRateToUSD,
        TransactionPaymentMethod paymentMethod,
        Guid? bookingId = null,
        string? referenceNumber = null)
    {
        if (amount <= 0)
            throw new DomainException("مبلغ المعاملة يجب أن يكون أكبر من صفر.");
        if (exchangeRateToUSD <= 0)
            throw new DomainException("سعر الصرف غير صالح.");

        return new Transaction
        {
            PropertyId = propertyId,
            PayerId = payerId,
            ReceiverId = receiverId,
            TransactionType = transactionType,
            Amount = amount,
            CurrencyId = currencyId,
            AmountInUSD = amount / exchangeRateToUSD,
            ExchangeRateUsed = exchangeRateToUSD,
            PaymentMethod = paymentMethod,
            BookingId = bookingId,
            ReferenceNumber = referenceNumber,
            Status = TransactionStatus.Pending,
            TransactedAt = DateTime.UtcNow
        };
    }

    public void MarkCompleted() => Status = TransactionStatus.Completed;
    public void MarkFailed() => Status = TransactionStatus.Failed;
    public void MarkRefunded() => Status = TransactionStatus.Refunded;
}
