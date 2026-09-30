using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Users.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Users;

public sealed class ConsentRecordTests
{
    [Fact]
    public void Create_WithEmptyUserId_Throws()
    {
        Assert.Throws<DomainException>(() => ConsentRecord.Create(
            Guid.Empty, ConsentPolicyType.PrivacyPolicy, "1.0", ConsentSource.Web, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNoPolicyVersion_Throws(string? version)
    {
        Assert.Throws<DomainException>(() => ConsentRecord.Create(
            Guid.NewGuid(), ConsentPolicyType.PrivacyPolicy, version!, ConsentSource.Web, DateTime.UtcNow));
    }

    [Fact]
    public void Create_IsActiveByDefault()
    {
        var record = ConsentRecord.Create(
            Guid.NewGuid(), ConsentPolicyType.TermsOfService, "1.0", ConsentSource.Android, DateTime.UtcNow);

        Assert.True(record.IsActive);
        Assert.Null(record.WithdrawnAtUtc);
    }

    [Fact]
    public void Withdraw_SetsWithdrawnAtUtc_AndClearsIsActive()
    {
        var record = ConsentRecord.Create(
            Guid.NewGuid(), ConsentPolicyType.PrivacyPolicy, "1.0", ConsentSource.Web, DateTime.UtcNow.AddDays(-1));

        var withdrawnAt = DateTime.UtcNow;
        record.Withdraw(withdrawnAt);

        Assert.False(record.IsActive);
        Assert.Equal(withdrawnAt, record.WithdrawnAtUtc);
    }

    [Fact]
    public void Withdraw_CalledTwice_KeepsTheFirstWithdrawalTimestamp()
    {
        // The audit trail must never silently move once written: a second withdrawal call
        // (e.g. a retried request) must not overwrite the original withdrawal time.
        var record = ConsentRecord.Create(
            Guid.NewGuid(), ConsentPolicyType.PrivacyPolicy, "1.0", ConsentSource.Web, DateTime.UtcNow.AddDays(-2));

        var firstWithdrawal = DateTime.UtcNow.AddDays(-1);
        record.Withdraw(firstWithdrawal);
        record.Withdraw(DateTime.UtcNow);

        Assert.Equal(firstWithdrawal, record.WithdrawnAtUtc);
    }
}
