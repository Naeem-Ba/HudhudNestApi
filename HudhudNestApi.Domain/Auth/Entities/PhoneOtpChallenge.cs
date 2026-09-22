using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Domain.Auth.Entities;

public sealed class PhoneOtpChallenge
{
    private PhoneOtpChallenge() { }
    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string NormalizedPhoneNumber { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public OtpPurpose Purpose { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public Guid? ReservationId { get; private set; }
    public DateTimeOffset? ReservedUntilUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static PhoneOtpChallenge Create(string phone, string hash, OtpPurpose purpose,
        DateTimeOffset now, Guid? userId = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NormalizedPhoneNumber = phone,
            CodeHash = hash,
            Purpose = purpose,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(5)
        };

    public void IncrementAttempts() => AttemptCount++;

    public void Reserve(Guid reservationId, DateTimeOffset untilUtc)
    {
        ReservationId = reservationId;
        ReservedUntilUtc = untilUtc;
    }

    public void Consume(DateTimeOffset nowUtc)
    {
        ConsumedAtUtc = nowUtc;
        Release();
    }

    public void Release()
    {
        ReservationId = null;
        ReservedUntilUtc = null;
    }
}
