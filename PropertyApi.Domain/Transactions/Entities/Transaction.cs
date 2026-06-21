using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.Transactions.Entities;

/// <summary>
/// سجل مالي لكل عملية مرتبطة بعقار.
/// لماذا؟ لضمان مسار مراجعة مالي كامل (Audit Trail).
/// ExchangeRateUsed مهم جداً: يحفظ سعر الصرف وقت العملية
/// حتى لو تغيّر السعر لاحقاً.
/// </summary>
public class Transaction : BaseEntity
{
    public Guid? BookingId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid PayerId { get; private set; }
    public Guid ReceiverId { get; private set; }

    /// <summary>Deposit / RentPayment / SalePayment / Refund / CommissionFee</summary>
    public string TransactionType { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public int CurrencyId { get; private set; }

    /// <summary>المبلغ بالدولار — يُحسب فورياً من سعر الصرف الحالي ويُخزَّن</summary>
    public decimal AmountInUSD { get; private set; }

    /// <summary>
    /// سعر الصرف وقت تنفيذ العملية.
    /// هذا مختلف عن BasePriceInUSD في Property:
    /// هنا نحفظه لأن المعاملة المالية تحتاج snapshot تاريخية.
    /// </summary>
    public decimal ExchangeRateUsed { get; private set; }

    /// <summary>Cash / BankTransfer / SyriatelCash / Online</summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>Pending / Completed / Failed / Refunded</summary>
    public string Status { get; private set; } = "Pending";

    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime TransactedAt { get; private set; }

    private Transaction() { }

    public static Transaction Create(
        Guid propertyId,
        Guid payerId,
        Guid receiverId,
        string transactionType,
        decimal amount,
        int currencyId,
        decimal exchangeRateToUSD,
        string paymentMethod,
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
            Status = "Pending",
            TransactedAt = DateTime.UtcNow
        };
    }

    public void MarkCompleted() => Status = "Completed";
    public void MarkFailed() => Status = "Failed";
    public void MarkRefunded() => Status = "Refunded";
}