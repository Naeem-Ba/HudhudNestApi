namespace HudhudNestApi.Auth.Tests.Domain;

[Trait("Category", "Domain")]
[Trait("Entity", "PhoneOtpChallenge")]
public sealed class PhoneOtpChallengeTests
{
    [Fact(DisplayName = "Create: valid inputs set challenge state")]
    public void Create_ValidInputs_SetsChallengeState()
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();

        var challenge = PhoneOtpChallenge.Create(
            "+963911234567",
            "hash-value",
            OtpPurpose.PhoneRegistration,
            now,
            userId);

        Assert.NotEqual(Guid.Empty, challenge.Id);
        Assert.Equal(userId, challenge.UserId);
        Assert.Equal("+963911234567", challenge.NormalizedPhoneNumber);
        Assert.Equal("hash-value", challenge.CodeHash);
        Assert.Equal(OtpPurpose.PhoneRegistration, challenge.Purpose);
        Assert.Equal(now, challenge.CreatedAtUtc);
        Assert.Equal(now.AddMinutes(5), challenge.ExpiresAtUtc);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.Null(challenge.ConsumedAtUtc);
        Assert.Null(challenge.ReservationId);
        Assert.Null(challenge.ReservedUntilUtc);
    }

    [Fact(DisplayName = "Create: the channel defaults to SMS and can be set to another")]
    public void Create_Channel_DefaultsToSms_AndKeepsTheRequestedOne()
    {
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

        var sms = PhoneOtpChallenge.Create("+963911234567", "h", OtpPurpose.PhoneRegistration, now);
        var telegram = PhoneOtpChallenge.Create("+963911234567", "h", OtpPurpose.PhoneRegistration, now, null, OtpChannel.Telegram);
        telegram.SetProviderRequestId("req-1");

        Assert.Equal(OtpChannel.Sms, sms.Channel);
        Assert.Null(sms.ProviderRequestId);
        Assert.Equal(OtpChannel.Telegram, telegram.Channel);
        Assert.Equal("req-1", telegram.ProviderRequestId);
    }

    [Fact(DisplayName = "Reserve and consume: tracks reservation and releases it on consume")]
    public void Reserve_And_Consume_TracksAndReleasesReservation()
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        var challenge = PhoneOtpChallenge.Create(
            "+963911234567",
            "hash-value",
            OtpPurpose.PhoneNumberChange,
            now);
        var reservationId = Guid.NewGuid();
        var reservedUntil = now.AddSeconds(30);

        challenge.IncrementAttempts();
        challenge.Reserve(reservationId, reservedUntil);

        Assert.Equal(1, challenge.AttemptCount);
        Assert.Equal(reservationId, challenge.ReservationId);
        Assert.Equal(reservedUntil, challenge.ReservedUntilUtc);

        var consumedAt = now.AddSeconds(10);
        challenge.Consume(consumedAt);

        Assert.Equal(consumedAt, challenge.ConsumedAtUtc);
        Assert.Null(challenge.ReservationId);
        Assert.Null(challenge.ReservedUntilUtc);
    }

    [Fact(DisplayName = "Release: clears reservation without consuming challenge")]
    public void Release_ClearsReservation_WithoutConsumingChallenge()
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        var challenge = PhoneOtpChallenge.Create(
            "+963911234567",
            "hash-value",
            OtpPurpose.PhoneReverification,
            now);

        challenge.Reserve(Guid.NewGuid(), now.AddSeconds(30));
        challenge.Release();

        Assert.Null(challenge.ReservationId);
        Assert.Null(challenge.ReservedUntilUtc);
        Assert.Null(challenge.ConsumedAtUtc);
    }
}
