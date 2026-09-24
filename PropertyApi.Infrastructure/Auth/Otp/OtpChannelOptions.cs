using PropertyApi.Domain.Enums;

namespace PropertyApi.Infrastructure.Auth.Otp;

/// <summary>
/// OtpChannels:* — which delivery channels exist, where each one does not work, and which one to suggest.
/// Everything the picker shows comes from here, so it can change without a new app release.
/// </summary>
public sealed class OtpChannelOptions
{
    public const string SectionName = "OtpChannels";

    /// <summary>SMS is on unless switched off explicitly.</summary>
    public OtpChannelSettings Sms { get; init; } = new();

    /// <summary>Telegram Gateway is off until <c>OtpChannels:Telegram:Enabled</c> is true and credentials exist.</summary>
    public OtpChannelSettings Telegram { get; init; } = new();

    /// <summary>WhatsApp Business Platform is off until <c>OtpChannels:WhatsApp:Enabled</c> is true and credentials exist.</summary>
    public OtpChannelSettings WhatsApp { get; init; } = new();

    /// <summary>The channel to suggest when no <see cref="RecommendedByCountryCode"/> entry matches. Empty = none.</summary>
    public string DefaultRecommended { get; init; } = string.Empty;

    /// <summary>Calling code (for example "+963") to the channel name to suggest for numbers starting with it.</summary>
    public Dictionary<string, string> RecommendedByCountryCode { get; init; } = new();

    /// <summary>
    /// Countries where the official WhatsApp Business Platform cannot deliver (Meta's eligibility list: Syria,
    /// Cuba, Iran, North Korea). Used when OtpChannels:WhatsApp:UnavailableCountryCodes is not set; setting it
    /// replaces this list. Sanctioned Ukrainian regions cannot be expressed as a calling-code prefix.
    /// </summary>
    public static readonly string[] DefaultWhatsAppUnavailableCountryCodes = ["+963", "+53", "+98", "+850"];

    public OtpChannelSettings For(OtpChannel channel) => channel switch
    {
        OtpChannel.Telegram => Telegram,
        OtpChannel.WhatsApp => WhatsApp,
        _ => Sms
    };

    public bool IsEnabled(OtpChannel channel) => For(channel).Enabled ?? channel == OtpChannel.Sms;

    public IReadOnlyList<string> UnavailableCountryCodes(OtpChannel channel)
    {
        var configured = For(channel).UnavailableCountryCodes;
        var codes = configured ?? (channel == OtpChannel.WhatsApp ? DefaultWhatsAppUnavailableCountryCodes : []);
        return codes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
    }
}

public sealed class OtpChannelSettings
{
    /// <summary>Null = the channel's default (on for SMS, off for the others). Nullable so "unset" is distinguishable.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Calling-code prefixes where this channel does not work. Null = the channel's built-in default.</summary>
    public string[]? UnavailableCountryCodes { get; init; }
}
