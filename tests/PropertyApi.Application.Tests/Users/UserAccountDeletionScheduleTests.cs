using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.Users;

/// <summary>
/// Domain-level unit tests for UserAccount's deletion-schedule lifecycle
/// (RequestDeletion/CancelDeletionRequest/HasPendingDeletionRequest -- Finding F7,
/// docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md) -- no mocks needed, pure entity behavior.
/// Mirrors UserAccountSubscriptionTests' sibling coverage style.
/// </summary>
public sealed class UserAccountDeletionScheduleTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    [Fact]
    public void RequestDeletion_SetsRequestedAtAndScheduledFor()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        var scheduledFor = account.RequestDeletion(ThirtyDays, Now);

        Assert.Equal(Now, account.DeletionRequestedAt);
        Assert.Equal(Now.Add(ThirtyDays), account.DeletionScheduledFor);
        Assert.Equal(Now.Add(ThirtyDays), scheduledFor);
        Assert.True(account.HasPendingDeletionRequest);
        Assert.False(account.IsDeleted);
    }

    [Fact]
    public void RequestDeletion_CalledTwice_DoesNotPushTheDateOut()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        var firstSchedule = account.RequestDeletion(ThirtyDays, Now);

        // A second request five days later must not extend the window -- the account owner
        // gets the schedule from the *first* request, not indefinitely renewable.
        var laterNow = Now.AddDays(5);
        var secondSchedule = account.RequestDeletion(ThirtyDays, laterNow);

        Assert.Equal(firstSchedule, secondSchedule);
        Assert.Equal(Now, account.DeletionRequestedAt);
        Assert.Equal(Now.Add(ThirtyDays), account.DeletionScheduledFor);
    }

    [Fact]
    public void RequestDeletion_WithNonPositiveDelay_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<ArgumentException>(() => account.RequestDeletion(TimeSpan.Zero, Now));
    }

    [Fact]
    public void RequestDeletion_OnAlreadyDeletedAccount_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.Anonymize(Now);

        Assert.Throws<InvalidOperationException>(() => account.RequestDeletion(ThirtyDays, Now));
    }

    [Fact]
    public void CancelDeletionRequest_ClearsSchedule()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.RequestDeletion(ThirtyDays, Now);

        account.CancelDeletionRequest(Now.AddDays(1));

        Assert.Null(account.DeletionRequestedAt);
        Assert.Null(account.DeletionScheduledFor);
        Assert.False(account.HasPendingDeletionRequest);
        Assert.False(account.IsDeleted);
    }

    [Fact]
    public void CancelDeletionRequest_WithNoPendingRequest_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<InvalidOperationException>(() => account.CancelDeletionRequest(Now));
    }

    [Fact]
    public void Anonymize_LeavesDeletionScheduleFieldsAsHistoricalRecord()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        var scheduledFor = account.RequestDeletion(ThirtyDays, Now);

        account.Anonymize(scheduledFor);

        Assert.True(account.IsDeleted);
        // Deliberately not cleared -- see Anonymize's doc comment and
        // DeletionScheduledFor's own doc comment on why these survive execution.
        Assert.Equal(Now, account.DeletionRequestedAt);
        Assert.Equal(scheduledFor, account.DeletionScheduledFor);
        // HasPendingDeletionRequest must go false once actually deleted, even though the
        // schedule fields are retained -- otherwise the sweep could pick the same account up
        // twice.
        Assert.False(account.HasPendingDeletionRequest);
    }
}
