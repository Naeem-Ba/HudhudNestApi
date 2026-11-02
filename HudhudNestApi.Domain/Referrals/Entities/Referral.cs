using System.Security.Cryptography;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.Referrals.Entities;

public enum ReferralStatus
{
    /// <summary>The new account signed up with the code; nothing qualifying has happened yet.</summary>
    Pending = 1,

    /// <summary>The new account did the qualifying activity and passed the fraud checks.</summary>
    Qualified = 2,

    /// <summary>A fraud check failed, the account went away, or an admin rejected it.</summary>
    Rejected = 3,

    /// <summary>Not qualified within the window.</summary>
    Expired = 4,
}

/// <summary>A user's personal invitation code (growth engine P5). One per user, created on first request.</summary>
public sealed class ReferralCode
{
    public const int CodeLength = 8;

    // No 0/O, 1/I/L: codes are read aloud and typed from screenshots.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public Guid UserId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    private ReferralCode() { }

    public static ReferralCode Generate(Guid userId, DateTime nowUtc)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new ReferralCode { UserId = userId, Code = new string(chars), CreatedAt = nowUtc };
    }

    /// <summary>Upper-cases and trims user input; anything that cannot be a code returns null.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var code = input.Trim().ToUpperInvariant();
        if (code.Length != CodeLength)
            return null;
        foreach (var c in code)
        {
            if (!Alphabet.Contains(c))
                return null;
        }
        return code;
    }
}

/// <summary>
/// One invited account (growth engine P5). Created at sign-up when a valid code was given; a user can be
/// referred once. It is qualified only by real activity, checked by a background job, never by the client.
/// </summary>
public sealed class Referral
{
    public const int ReasonMaxLength = 300;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReferrerUserId { get; private set; }
    public Guid ReferredUserId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public ReferralStatus Status { get; private set; } = ReferralStatus.Pending;
    public DateTime CreatedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }

    /// <summary>Stable reason code for a rejection or expiry, e.g. <c>same_phone</c>, <c>monthly_cap</c>.</summary>
    public string? Reason { get; private set; }

    /// <summary>When an admin recorded that the reward was given (rewards are granted by hand until decided).</summary>
    public DateTime? RewardedAt { get; private set; }
    public Guid? RewardedByUserId { get; private set; }

    private Referral() { }

    public static Referral Create(Guid referrerUserId, Guid referredUserId, string code, DateTime nowUtc)
    {
        if (referrerUserId == Guid.Empty || referredUserId == Guid.Empty)
            throw new DomainException("Both users are required.");
        if (referrerUserId == referredUserId)
            throw new DomainException("A user cannot refer themselves.");

        return new Referral
        {
            ReferrerUserId = referrerUserId,
            ReferredUserId = referredUserId,
            Code = code,
            CreatedAt = nowUtc,
        };
    }

    public void Qualify(DateTime nowUtc)
    {
        if (Status != ReferralStatus.Pending)
            return;
        Status = ReferralStatus.Qualified;
        DecidedAt = nowUtc;
        Reason = null;
    }

    public void Reject(string reason, DateTime nowUtc)
    {
        if (Status is ReferralStatus.Rejected || RewardedAt is not null)
            return;
        Status = ReferralStatus.Rejected;
        DecidedAt = nowUtc;
        Reason = Cut(reason);
    }

    public void Expire(DateTime nowUtc)
    {
        if (Status != ReferralStatus.Pending)
            return;
        Status = ReferralStatus.Expired;
        DecidedAt = nowUtc;
        Reason = "not_qualified_in_time";
    }

    public void MarkRewarded(Guid adminUserId, DateTime nowUtc)
    {
        if (Status != ReferralStatus.Qualified)
            throw new DomainException("Only a qualified referral can be marked as rewarded.");
        if (RewardedAt is not null)
            return;
        RewardedAt = nowUtc;
        RewardedByUserId = adminUserId;
    }

    private static string Cut(string value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? "rejected" : value.Trim();
        return v.Length > ReasonMaxLength ? v[..ReasonMaxLength] : v;
    }
}
