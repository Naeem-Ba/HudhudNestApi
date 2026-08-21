namespace PropertyApi.Domain.Transactions.Enums;

/// <summary>
/// Payment rail used to settle a Transaction. Values reflect the payment
/// methods actually available in the Syrian market at the time this enum
/// was introduced (see Phase-0 tech-debt cleanup notes).
/// </summary>
public enum TransactionPaymentMethod
{
    Cash = 0,
    BankTransfer = 1,
    SyriatelCash = 2,
    Online = 3,
}
