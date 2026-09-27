namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// SocialDistribution:Telegram — credentials and transport settings for the real Telegram Bot API
/// integration (Phase 2). Distinct from <c>TelegramGatewayOptions</c> (Auth/Otp): that is the
/// official Telegram Gateway product for phone-number OTP delivery; this is the official Bot API
/// (<c>https://core.telegram.org/bots/api</c>) used to post to a channel this account administers
/// — same company, two unrelated products, never sharing a token or a section.
/// </summary>
public sealed class TelegramBotOptions
{
    public const string SectionName = "SocialDistribution:Telegram";

    /// <summary>The bot token from @BotFather. Sent only in the request URL path (Bot API's own
    /// convention — there is no header-based auth for this API), never logged, never persisted
    /// anywhere but this configuration value.</summary>
    public string BotToken { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.telegram.org/";

    public int TimeoutSeconds { get; init; } = 15;
}
