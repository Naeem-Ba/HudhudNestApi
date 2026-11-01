using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Referrals.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Referrals;

public sealed class ReferralDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Codes_are_eight_unambiguous_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = ReferralCode.Generate(Guid.NewGuid(), Now).Code;
            Assert.Equal(8, code.Length);
            Assert.DoesNotContain(code, c => "01OIL".Contains(c));
            Assert.Equal(code, ReferralCode.Normalize(" " + code.ToLowerInvariant() + " "));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDEFG0")]
    [InlineData("ABCDEFGHJ")]
    public void Invalid_codes_normalize_to_null(string? input) => Assert.Null(ReferralCode.Normalize(input));

    [Fact]
    public void Self_referral_is_refused()
    {
        var id = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Referral.Create(id, id, "ABCDEFGH", Now));
    }

    [Fact]
    public void Lifecycle_rules()
    {
        var r = Referral.Create(Guid.NewGuid(), Guid.NewGuid(), "ABCDEFGH", Now);
        Assert.Throws<DomainException>(() => r.MarkRewarded(Guid.NewGuid(), Now));

        r.Qualify(Now);
        Assert.Equal(ReferralStatus.Qualified, r.Status);
        r.Expire(Now); // only pending expires
        Assert.Equal(ReferralStatus.Qualified, r.Status);

        r.MarkRewarded(Guid.NewGuid(), Now);
        Assert.NotNull(r.RewardedAt);
        r.Reject("late", Now); // a recorded reward is not undone
        Assert.Equal(ReferralStatus.Qualified, r.Status);
    }
}
