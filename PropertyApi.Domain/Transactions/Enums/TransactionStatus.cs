namespace PropertyApi.Domain.Transactions.Enums;

/// <summary>
/// Lifecycle state of a Transaction. Mirrors the state transitions already
/// implemented by Transaction.MarkCompleted() / MarkFailed() / MarkRefunded().
/// </summary>
public enum TransactionStatus
{
    /// <summary>Created but not yet settled. Default state on Transaction.Create().</summary>
    Pending = 0,

    /// <summary>Settled successfully.</summary>
    Completed = 1,

    /// <summary>Attempted but did not settle.</summary>
    Failed = 2,

    /// <summary>Previously completed, later reversed.</summary>
    Refunded = 3,
}
