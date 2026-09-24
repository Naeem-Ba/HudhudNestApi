using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Services;

public interface IPhoneVerificationPolicy
{
    PhoneVerificationState Evaluate(DateTimeOffset lastVerifiedAtUtc, DateTimeOffset nowUtc);
}

public sealed class PhoneVerificationPolicy : IPhoneVerificationPolicy
{
    /// <summary>How long a verification lasts before the number must be verified again.</summary>
    public static readonly TimeSpan VerificationInterval = TimeSpan.FromDays(180);

    /// <summary>Time after the due date before the account is restricted.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(3);

    /// <summary>Time before the due date during which the user is reminded.</summary>
    public static readonly TimeSpan DueSoonWindow = TimeSpan.FromDays(14);

    public PhoneVerificationState Evaluate(DateTimeOffset verified, DateTimeOffset now)
        => Evaluate(verified + VerificationInterval, verified + VerificationInterval + GracePeriod, now);

    /// <summary>State from stored due / grace-end dates (the reminder worker reads them per user).</summary>
    public static PhoneVerificationState Evaluate(DateTimeOffset due, DateTimeOffset graceEnds, DateTimeOffset now)
    {
        if (now >= graceEnds) return PhoneVerificationState.Restricted;
        if (now >= due) return PhoneVerificationState.GracePeriod;
        if (now >= due - DueSoonWindow) return PhoneVerificationState.DueSoon;
        return PhoneVerificationState.Verified;
    }
}
