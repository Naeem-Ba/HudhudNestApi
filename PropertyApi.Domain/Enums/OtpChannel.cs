namespace PropertyApi.Domain.Enums;

/// <summary>
/// How a phone OTP reaches the person. The phone number stays the identity; the channel is only the delivery
/// mechanism of one challenge, so choosing another channel never creates another user.
/// Numeric values are stored as text (see PhoneOtpChallengeConfiguration); add new members at the end.
/// </summary>
public enum OtpChannel
{
    Sms = 0,
    Telegram = 1,
    WhatsApp = 2
}
