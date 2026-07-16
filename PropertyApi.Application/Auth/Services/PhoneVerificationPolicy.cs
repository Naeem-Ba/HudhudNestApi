using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Services;

public interface IPhoneVerificationPolicy
{
    PhoneVerificationState Evaluate(DateTimeOffset lastVerifiedAtUtc, DateTimeOffset nowUtc);
}

public sealed class PhoneVerificationPolicy : IPhoneVerificationPolicy
{
    public PhoneVerificationState Evaluate(DateTimeOffset verified, DateTimeOffset now)
    {
        var due = verified.AddDays(180);
        if (now >= due.AddDays(3)) return PhoneVerificationState.Restricted;
        if (now >= due) return PhoneVerificationState.GracePeriod;
        if (now >= due.AddDays(-14)) return PhoneVerificationState.DueSoon;
        return PhoneVerificationState.Verified;
    }
}
