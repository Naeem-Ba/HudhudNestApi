using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Phone;

/// <summary>What happened when a provider was asked to deliver one code.</summary>
public enum OtpSendOutcome
{
    /// <summary>The provider accepted the message.</summary>
    Sent = 0,

    /// <summary>
    /// The provider answered, but this number cannot receive the message on this channel (for example the number
    /// is not on Telegram). It is a fact about the number, so it is never shown to the caller as such: doing so
    /// would tell anyone probing the endpoint whether a number holds an account.
    /// </summary>
    RecipientUnreachable = 1,

    /// <summary>The provider could not be used: refused our credentials, timed out, or failed on its side.</summary>
    ProviderUnavailable = 2,

    /// <summary>The channel is switched off or has no provider registered.</summary>
    ChannelUnavailable = 3
}

public readonly record struct OtpSendResult(OtpSendOutcome Outcome, string? ProviderRequestId = null)
{
    public static OtpSendResult Sent(string? providerRequestId = null) => new(OtpSendOutcome.Sent, providerRequestId);
    public static OtpSendResult Unreachable() => new(OtpSendOutcome.RecipientUnreachable);
    public static OtpSendResult Unavailable() => new(OtpSendOutcome.ProviderUnavailable);
}

/// <summary>One way of delivering a code. Business logic never branches on the channel; it asks <see cref="IOtpChannelService"/>.</summary>
public interface IOtpProvider
{
    OtpChannel Channel { get; }

    /// <summary>Delivers <paramref name="code"/> to <paramref name="phoneNumber"/> (E.164). Must not throw for provider failures.</summary>
    Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct);
}

public static class OtpChannelUnavailableReasons
{
    public const string CountryNotSupported = "COUNTRY_NOT_SUPPORTED";
    public const string ChannelDisabled = "CHANNEL_DISABLED";
}

/// <summary>
/// One row of the channel picker. <see cref="UnavailableCountryCodes"/> is the configured list, for the fixed
/// "not available in: ..." text; <see cref="Available"/> is the answer for the phone number that was asked about.
/// </summary>
public sealed record OtpChannelAvailability(
    OtpChannel Channel,
    bool Available,
    bool Recommended,
    string? Reason,
    IReadOnlyList<string> UnavailableCountryCodes);

public interface IOtpChannelService
{
    /// <summary>
    /// Every channel with its availability for <paramref name="normalizedPhone"/> (null = no number yet, so only
    /// switched-off channels are unavailable). Depends on the public calling code and configuration only, never on
    /// whether an account exists.
    /// </summary>
    IReadOnlyList<OtpChannelAvailability> Describe(string? normalizedPhone);

    bool IsAvailable(OtpChannel channel, string normalizedPhone);

    Task<OtpSendResult> SendAsync(OtpChannel channel, string normalizedPhone, string code, CancellationToken ct);
}
