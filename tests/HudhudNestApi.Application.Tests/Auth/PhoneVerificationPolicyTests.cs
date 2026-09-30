using HudhudNestApi.Application.Auth.Services;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Tests.Auth;

public sealed class PhoneVerificationPolicyTests
{
    [Theory]
    [InlineData(165, PhoneVerificationState.Verified)]
    [InlineData(166, PhoneVerificationState.DueSoon)]
    [InlineData(180, PhoneVerificationState.GracePeriod)]
    [InlineData(183, PhoneVerificationState.Restricted)]
    public void Evaluate_UsesRequiredBoundaries(int days, PhoneVerificationState expected)
    {
        var verified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, new PhoneVerificationPolicy().Evaluate(verified, verified.AddDays(days)));
    }
}
