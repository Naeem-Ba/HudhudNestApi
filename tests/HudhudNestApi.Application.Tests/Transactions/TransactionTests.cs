using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Transactions.Entities;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Tests.Transactions;

/// <summary>
/// Covers the Phase-0 tech-debt cleanup: TransactionType / PaymentMethod / Status
/// moved from free-text strings to enums. These tests protect the invariants
/// Transaction.Create() and the Mark*() state-transition methods rely on.
/// </summary>
public sealed class TransactionTests
{
    [Fact]
    public void Create_SetsStatusToPending_ByDefault()
    {
        var transaction = Transaction.Create(
            propertyId: Guid.NewGuid(),
            payerId: Guid.NewGuid(),
            receiverId: Guid.NewGuid(),
            transactionType: TransactionType.RentPayment,
            amount: 100m,
            currencyId: 1,
            exchangeRateToUSD: 15000m,
            paymentMethod: TransactionPaymentMethod.Cash);

        Assert.Equal(TransactionStatus.Pending, transaction.Status);
        Assert.Equal(TransactionType.RentPayment, transaction.TransactionType);
        Assert.Equal(TransactionPaymentMethod.Cash, transaction.PaymentMethod);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_Throws_WhenAmountIsNotPositive(decimal invalidAmount)
    {
        Assert.Throws<DomainException>(() => Transaction.Create(
            propertyId: Guid.NewGuid(),
            payerId: Guid.NewGuid(),
            receiverId: Guid.NewGuid(),
            transactionType: TransactionType.Deposit,
            amount: invalidAmount,
            currencyId: 1,
            exchangeRateToUSD: 15000m,
            paymentMethod: TransactionPaymentMethod.Cash));
    }

    [Fact]
    public void Create_Throws_WhenExchangeRateIsNotPositive()
    {
        Assert.Throws<DomainException>(() => Transaction.Create(
            propertyId: Guid.NewGuid(),
            payerId: Guid.NewGuid(),
            receiverId: Guid.NewGuid(),
            transactionType: TransactionType.Deposit,
            amount: 100m,
            currencyId: 1,
            exchangeRateToUSD: 0m,
            paymentMethod: TransactionPaymentMethod.Cash));
    }

    [Fact]
    public void MarkCompleted_TransitionsStatusToCompleted()
    {
        var transaction = CreateValidTransaction();

        transaction.MarkCompleted();

        Assert.Equal(TransactionStatus.Completed, transaction.Status);
    }

    [Fact]
    public void MarkFailed_TransitionsStatusToFailed()
    {
        var transaction = CreateValidTransaction();

        transaction.MarkFailed();

        Assert.Equal(TransactionStatus.Failed, transaction.Status);
    }

    [Fact]
    public void MarkRefunded_TransitionsStatusToRefunded()
    {
        var transaction = CreateValidTransaction();
        transaction.MarkCompleted();

        transaction.MarkRefunded();

        Assert.Equal(TransactionStatus.Refunded, transaction.Status);
    }

    [Fact]
    public void Create_ComputesAmountInUSD_FromExchangeRate()
    {
        var transaction = Transaction.Create(
            propertyId: Guid.NewGuid(),
            payerId: Guid.NewGuid(),
            receiverId: Guid.NewGuid(),
            transactionType: TransactionType.SalePayment,
            amount: 150_000m,
            currencyId: 1,
            exchangeRateToUSD: 15000m,
            paymentMethod: TransactionPaymentMethod.BankTransfer);

        Assert.Equal(10m, transaction.AmountInUSD);
    }

    private static Transaction CreateValidTransaction() =>
        Transaction.Create(
            propertyId: Guid.NewGuid(),
            payerId: Guid.NewGuid(),
            receiverId: Guid.NewGuid(),
            transactionType: TransactionType.RentPayment,
            amount: 100m,
            currencyId: 1,
            exchangeRateToUSD: 15000m,
            paymentMethod: TransactionPaymentMethod.Cash);
}
